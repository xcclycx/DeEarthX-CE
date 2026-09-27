using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Crypto;
using DeEarthX.Infrastructure.Zip;

namespace DeEarthX.Plugins;

/// <summary>
/// 插件系统的门面（facade）。
/// <para>
/// 职责边界：只做“协调 + 缓存 + 一致性”，具体能力下沉到
/// <see cref="IPluginLoader"/>（读）、<see cref="IPluginConfigStore"/>（存）、
/// <see cref="PluginHookDispatcher"/>（跑）、<see cref="PluginTemplateGenerator"/>（脚手架）。
/// 因此新增一种插件能力时通常不需要改动本类。
/// </para>
/// <para>本类同时保证三件事：</para>
/// <list type="number">
///   <item><b>单次加载</b>：并发的首次访问共享同一次扫描，不再重复读盘（旧实现每个并发请求都会全量重扫）；</item>
///   <item><b>快照一致</b>：查询返回不可变列表且顺序稳定，遍历时不受增删影响；</item>
///   <item><b>变更可见</b>：任何写操作都会推进 <see cref="Version"/>，钩子执行计划据此失效重建。</item>
/// </list>
/// </summary>
public sealed class PluginManager : IAsyncDisposable, IDisposable
{
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions Json = DeEarthXJsonOptions.Default;

    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly ILogService _logService;
    private readonly IDexpCrypto _dexpCrypto;
    private readonly IZipService _zipService;
    private readonly IPluginLoader _loader;
    private readonly IPluginConfigStore _configStore;
    private readonly PluginValidator _validator;
    private readonly IPluginTemplateGenerator _templateGenerator;
    private readonly IPluginEventBus _events;
    private readonly PluginExtensionCatalog _extensions;

    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PluginLifecycleScripts> _lifecycleScripts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IPluginLifecycle> _inProcessPlugins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PageCacheEntry> _pageCache = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    private List<LoadedPlugin>? _snapshot;
    private Task<PluginLoadReport>? _loadTask;
    private PluginLoadReport _lastReport = PluginLoadReport.Empty;
    private long _version;
    private bool _hotReload;
    private bool _disposed;

    public PluginManager(
        IAppDirectoryProvider appDirectoryProvider,
        ILogService logService,
        IDexpCrypto dexpCrypto,
        IZipService zipService,
        IPluginLoader loader,
        IPluginConfigStore configStore,
        PluginValidator validator,
        IPluginTemplateGenerator templateGenerator,
        IPluginEventBus events,
        PluginExtensionCatalog extensions)
    {
        _appDirectoryProvider = appDirectoryProvider;
        _logService = logService;
        _dexpCrypto = dexpCrypto;
        _zipService = zipService;
        _loader = loader;
        _configStore = configStore;
        _validator = validator;
        _templateGenerator = templateGenerator;
        _events = events;
        _extensions = extensions;
    }

    private string PluginsDir => Path.Combine(_appDirectoryProvider.GetAppDirectory(), "plugins");

    private string GlobalConfigsPath => Path.Combine(_appDirectoryProvider.GetAppDirectory(), "plugin-configs.json");

    /// <summary>插件集合版本号。任何增删改都会自增，钩子执行计划据此判断是否需要重建。</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>最近一次扫描的结果（含失败清单与耗时）。</summary>
    public PluginLoadReport LastReport => _lastReport;

    /// <summary>是否启用插件目录热重载。默认开启，便于边改边看。</summary>
    public bool HotReloadEnabled => _hotReload;

    // ------------------------------------------------------------------ 查询

    public async Task<List<LoadedPlugin>> GetPluginsAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return new List<LoadedPlugin>(snapshot);
    }

    /// <summary>同步取快照，供执行计划构建等热路径使用（首次会同步加载）。</summary>
    public IReadOnlyList<LoadedPlugin> SnapshotPlugins()
    {
        if (_snapshot is { } cached && _loadTask is null)
        {
            return cached;
        }

        // 首次加载需要读盘，用同步等待避免把异步传染到钩子执行计划里。
        return GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<LoadedPlugin?> GetPluginAsync(string id, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.FirstOrDefault(p => string.Equals(p.Manifest.Id, id, StringComparison.Ordinal));
    }

    public async Task<LoadedPlugin> GetPluginForApiAsync(string id, CancellationToken ct = default)
        => await GetPluginAsync(id, ct).ConfigureAwait(false) ?? throw new FileNotFoundException("插件不存在");

    public string GetPluginsDir() => PluginsDir;

    /// <summary>诊断视图：加载状态、失败原因、耗时、扩展点，供前端与排查使用。</summary>
    public async Task<PluginDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return new PluginDiagnostics
        {
            Total = snapshot.Count,
            Enabled = snapshot.Count(p => p.Config.Enabled),
            LoadedInMilliseconds = _lastReport.ElapsedMilliseconds,
            HotReload = _hotReload,
            Plugins = snapshot.Select(p => new PluginDiagnosticsEntry
            {
                Id = p.Manifest.Id,
                Name = p.Manifest.Name,
                Version = p.Manifest.Version,
                State = p.State.ToString(),
                Enabled = p.Config.Enabled,
                AutoStart = p.Config.AutoStart,
                Error = p.LoadError,
                Dependencies = p.Manifest.Dependencies.Select(d => d.Id).ToList(),
                ResolvedDependencies = p.ResolvedDependencies,
                Hooks = p.Manifest.Capabilities.Hooks
            }).ToList(),
            Failures = _lastReport.Failures.Select(f => new PluginDiagnosticsEntry
            {
                Id = f.Id,
                State = f.State.ToString(),
                Error = f.Error,
                Directory = f.DirectoryPath
            }).ToList(),
            ExtensionPoints = _extensions.Describe().ToList(),
            DeclaredHooks = await GetDeclaredHooksAsync(ct).ConfigureAwait(false)
        };
    }

    private async Task<IReadOnlyList<string>> GetDeclaredHooksAsync(CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        var hooks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in snapshot)
        {
            foreach (var hook in plugin.Manifest.Capabilities.Hooks)
            {
                hooks.Add(hook);
            }
        }

        return hooks.ToList();
    }

    // ------------------------------------------------------------------ 状态变更

    public async Task EnableAsync(string id, CancellationToken ct = default)
        => await SetEnabledAsync(id, true, ct).ConfigureAwait(false);

    public async Task DisableAsync(string id, CancellationToken ct = default)
        => await SetEnabledAsync(id, false, ct).ConfigureAwait(false);

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var plugin = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            plugin.Config.Enabled = enabled;
            await _configStore.SaveAsync(id, plugin.Config, ct).ConfigureAwait(false);
            WriteGlobalPluginConfigs();
            AdvanceVersion();
        }
        finally
        {
            _mutationGate.Release();
        }

        _logService.Info(enabled ? $"插件已启用: {plugin.Manifest.Name}" : $"插件已禁用: {plugin.Manifest.Name}");

        if (enabled)
        {
            await StartInProcessAsync(plugin, ct).ConfigureAwait(false);
        }
        else
        {
            await StopInProcessAsync(plugin, ct).ConfigureAwait(false);
        }

        await PublishStateChangedAsync(plugin, enabled, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 暂停/恢复插件参与流水线，但不改变“已启用”状态与配置。
    /// 用户排查插件冲突时非常有用：先暂停而不是卸载。
    /// </summary>
    public async Task SetAutoStartAsync(string id, bool autoStart, CancellationToken ct = default)
    {
        var plugin = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            plugin.Config.AutoStart = autoStart;
            await _configStore.SaveAsync(id, plugin.Config, ct).ConfigureAwait(false);
            AdvanceVersion();
        }
        finally
        {
            _mutationGate.Release();
        }

        if (autoStart && plugin.Config.Enabled)
        {
            await StartInProcessAsync(plugin, ct).ConfigureAwait(false);
        }
        else if (!autoStart)
        {
            await StopInProcessAsync(plugin, ct).ConfigureAwait(false);
        }

        _logService.Info(autoStart ? $"插件已恢复运行: {plugin.Manifest.Name}" : $"插件已暂停运行: {plugin.Manifest.Name}");
    }

    /// <summary>更新插件设置（合并语义：只覆盖提交上来的键）。</summary>
    public async Task UpdateSettingsAsync(string id, JsonNode? settings, CancellationToken ct = default)
    {
        var plugin = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (settings is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    plugin.Config.Settings[kv.Key] = kv.Value?.DeepClone();
                }
            }

            await _configStore.SaveAsync(id, plugin.Config, ct).ConfigureAwait(false);
            WriteGlobalPluginConfigs();
            AdvanceVersion();
        }
        finally
        {
            _mutationGate.Release();
        }

        await _events.PublishAsync(
            "PluginSettingsChanged",
            new PluginEventArgs
            {
                Payload = new { pluginId = id },
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal) { ["pluginId"] = id }
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>用清单默认值重置单个插件的设置。</summary>
    public async Task ResetSettingsAsync(string id, CancellationToken ct = default)
    {
        var plugin = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            plugin.Config.Settings.Clear();
            if (plugin.Manifest.DefaultConfig is JsonObject defaults)
            {
                foreach (var kv in defaults)
                {
                    plugin.Config.Settings[kv.Key] = kv.Value?.DeepClone();
                }
            }

            await _configStore.SaveAsync(id, plugin.Config, ct).ConfigureAwait(false);
            AdvanceVersion();
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    // ------------------------------------------------------------------ 创建 / 安装 / 卸载

    public Task<LoadedPlugin> CreatePluginAsync(string name, string author, string url = "", CancellationToken ct = default)
        => CreatePluginAsync(name, author, url, true, ct);

    public async Task<LoadedPlugin> CreatePluginAsync(string name, string author, string url, bool withTemplate, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PluginsDir);

        var id = BuildPluginId();
        var pluginDir = Path.Combine(PluginsDir, id);
        Directory.CreateDirectory(pluginDir);

        var manifest = new PluginManifest
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            Author = string.IsNullOrWhiteSpace(author) ? "Unknown" : author,
            Homepage = url,
            AutoStart = true,
            Capabilities = new PluginCapabilities
            {
                // 带模板时立即给出一个可访问的示例页面，避免“创建完什么都没有”的困惑。
                Sidebar = withTemplate,
                Pages = withTemplate
            },
            Resources = new PluginResources
            {
                Files = new List<string>(),
                Scripts = new List<string>()
            }
        };

        if (withTemplate)
        {
            manifest.SidebarItems = new List<PluginSidebarItem>
            {
                new() { Key = "main", Label = name, Icon = "🧩", Route = "index" }
            };
        }

        await WriteManifestAsync(pluginDir, manifest, ct).ConfigureAwait(false);

        if (withTemplate)
        {
            await _templateGenerator.GenerateAsync(pluginDir, manifest, ct).ConfigureAwait(false);
        }

        // 直接走一次增量装载，避免为了一个插件重扫整个目录。
        await LoadIntoAsync(pluginDir, ct).ConfigureAwait(false);

        var loaded = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);
        _logService.Info($"插件已创建: {name} ({id})");
        return loaded;
    }

    public async Task<PluginInstallResult> InstallSmartAsync(byte[] buffer, CancellationToken ct = default)
    {
        if (_dexpCrypto.IsDexp(buffer))
        {
            var header = _dexpCrypto.ParseHeader(buffer);
            if (header is null)
            {
                return new PluginInstallResult(null, "无效的 DEXP 文件", false);
            }

            if (header.Mode == DexpCrypto.PublicMode)
            {
                var decrypted = _dexpCrypto.Decrypt(buffer, DexpCrypto.PublicPassword);
                if (decrypted is null)
                {
                    return new PluginInstallResult(null, "DEXP 解密失败", false);
                }

                var loaded = await InstallFromZipAsync(decrypted, ct).ConfigureAwait(false);
                return new PluginInstallResult(loaded.Manifest.Id, null, false);
            }

            return new PluginInstallResult(null, "需要密码解密", true);
        }

        var plugin = await InstallFromZipAsync(buffer, ct).ConfigureAwait(false);
        return new PluginInstallResult(plugin.Manifest.Id, null, false);
    }

    public async Task<LoadedPlugin> InstallFromEncryptedAsync(byte[] encryptedBytes, string password, CancellationToken ct = default)
    {
        var decrypted = _dexpCrypto.Decrypt(encryptedBytes, password)
            ?? throw new InvalidOperationException("解密失败：密码错误或数据损坏");
        return await InstallFromZipAsync(decrypted, ct).ConfigureAwait(false);
    }

    public async Task<LoadedPlugin> InstallFromZipAsync(byte[] zipBytes, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PluginsDir);

        var topLevelDir = DetectTopLevelDirectory(zipBytes);
        var tempDir = Path.Combine(PluginsDir, $"_install_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            await _zipService.ExtractToDirectoryAsync(zipBytes, tempDir, ct).ConfigureAwait(false);

            var sourceDir = tempDir;
            if (topLevelDir is not null && Directory.Exists(Path.Combine(tempDir, topLevelDir)))
            {
                sourceDir = Path.Combine(tempDir, topLevelDir);
            }

            var manifestPath = Path.Combine(sourceDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("插件包中未找到 manifest.json");
            }

            var manifest = await ReadManifestFileAsync(manifestPath, ct).ConfigureAwait(false);
            var id = string.IsNullOrWhiteSpace(manifest.Id)
                ? Guid.NewGuid().ToString("N")[..10]
                : SanitizeId(manifest.Id);
            manifest.Id = id;

            var destDir = Path.Combine(PluginsDir, id);
            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, true);
            }

            // 直接移动已解压目录：旧实现再逐文件复制一遍，等于把每个文件写两次磁盘。
            try
            {
                Directory.Move(sourceDir, destDir);
            }
            catch (IOException)
            {
                CopyDirectory(sourceDir, destDir);
            }

            await LoadIntoAsync(destDir, ct).ConfigureAwait(false);
            var loaded = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

            _logService.Info($"插件已安装: {manifest.Name} ({id})");
            await PublishInstalledAsync(loaded, ct).ConfigureAwait(false);
            return loaded;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public async Task UninstallAsync(string id, bool keepConfig = true, CancellationToken ct = default)
    {
        var plugin = await GetPluginAsync(id, ct).ConfigureAwait(false);

        if (plugin is not null)
        {
            await StopInProcessAsync(plugin, ct).ConfigureAwait(false);
            await RunLifecycleScriptAsync(plugin, stop: true, ct).ConfigureAwait(false);
        }

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.Combine(PluginsDir, id);
            TryDeleteDirectory(dir);

            _plugins.TryRemove(id, out _);
            _lifecycleScripts.TryRemove(id, out _);
            _inProcessPlugins.TryRemove(id, out _);
            _extensions.RemoveOwner(id);
            InvalidatePageCache(id);

            _configStore.Invalidate();
            if (!keepConfig)
            {
                await _configStore.RemoveAsync(id, ct).ConfigureAwait(false);
            }

            WriteGlobalPluginConfigs();

            // 快照必须跟着 _plugins 一起更新，否则卸载后 /plugins 仍会把已删除的插件
            // 返回给前端（旧实现只推进版本号，快照要等到下一次热重载扫描才刷新）。
            _loadGate.Wait(ct);
            try
            {
                RebuildSnapshot();
            }
            finally
            {
                _loadGate.Release();
            }

            AdvanceVersion();
        }
        finally
        {
            _mutationGate.Release();
        }

        _logService.Info($"插件已卸载: {id} (keepConfig={keepConfig})");
    }

    public async Task ReloadPluginAsync(string id, CancellationToken ct = default)
    {
        var dir = Path.Combine(PluginsDir, id);
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException($"插件目录不存在: {id}");
        }

        if (!File.Exists(Path.Combine(dir, ManifestFileName)))
        {
            throw new FileNotFoundException("插件清单不存在");
        }

        var existing = await GetPluginAsync(id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await StopInProcessAsync(existing, ct).ConfigureAwait(false);
            await RunLifecycleScriptAsync(existing, stop: true, ct).ConfigureAwait(false);
        }

        await LoadIntoAsync(dir, ct).ConfigureAwait(false);
        var reloaded = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);
        _logService.Info($"插件已重新加载: {reloaded.Manifest.Name} ({id})");
    }

    /// <summary>清空缓存并重新扫描整个插件目录。</summary>
    public async Task<PluginLoadReport> ReloadAllAsync(CancellationToken ct = default)
    {
        _loadGate.Wait(ct);
        try
        {
            _snapshot = null;
            _loadTask = null;
        }
        finally
        {
            _loadGate.Release();
        }

        _configStore.Invalidate();
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return _lastReport with { Plugins = snapshot };
    }

    // ------------------------------------------------------------------ 校验与文件

    public PluginValidationResult ValidatePlugin(string id)
    {
        if (!_plugins.TryGetValue(id, out var plugin))
        {
            return new PluginValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "插件不存在" }
            };
        }

        var result = _validator.Validate(plugin.Manifest);
        if (result.IsValid)
        {
            result = _validator.ValidateConfig(plugin.Manifest, plugin.Config);
        }

        return result;
    }

    public async Task<List<string>> ListPluginFilesAsync(string id, CancellationToken ct = default)
    {
        await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        var dir = Path.Combine(PluginsDir, id);
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException($"插件目录不存在: {id}");
        }

        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            files.Add(Path.GetRelativePath(dir, file).Replace('\\', '/'));
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public async Task<byte[]> ExportZipAsync(string id, CancellationToken ct = default)
    {
        await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        var dir = Path.Combine(PluginsDir, id);
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException($"插件目录不存在: {id}");
        }

        // 直接写临时文件再读回，避免大插件包在内存里复制两份。
        var tempFile = Path.Combine(Path.GetTempPath(), $"deearthx_plugin_{id}_{Guid.NewGuid():N}.zip");
        try
        {
            await _zipService.CreateZipAsync(dir, tempFile, 9, ct).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(tempFile, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempFile);
        }
    }

    public async Task<byte[]> ExportEncryptedAsync(string id, string password, byte mode, CancellationToken ct = default)
    {
        var zip = await ExportZipAsync(id, ct).ConfigureAwait(false);
        return _dexpCrypto.Encrypt(zip, password, mode);
    }

    public string GetPluginFilePath(string id, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new UnauthorizedAccessException("路径越权访问被拒绝");
        }

        var pluginDir = Path.Combine(PluginsDir, id);
        return PluginPaths.ResolveInside(pluginDir, relativePath);
    }

    public async Task<string> ReadPluginPageAsync(string pluginId, string pageKey, CancellationToken ct = default)
    {
        var sanitizedKey = SanitizeId(pageKey);
        var cacheKey = $"{pluginId}/{sanitizedKey}";

        var candidates = new[]
        {
            Path.Combine(PluginsDir, pluginId, "frontend", $"{sanitizedKey}.html"),
            Path.Combine(PluginsDir, pluginId, "frontend", sanitizedKey, "index.html"),
            Path.Combine(PluginsDir, pluginId, $"{sanitizedKey}.html"),
            Path.Combine(PluginsDir, pluginId, "frontend", "index.html")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            // HTML 页面可能被前端反复请求；用 (写入时间, 长度) 做廉价缓存校验。
            var info = new FileInfo(candidate);
            if (_pageCache.TryGetValue(cacheKey, out var cached) &&
                cached.Path == candidate &&
                cached.LastWriteUtc == info.LastWriteTimeUtc &&
                cached.Length == info.Length)
            {
                return cached.Content;
            }

            var content = await File.ReadAllTextAsync(candidate, ct).ConfigureAwait(false);
            _pageCache[cacheKey] = new PageCacheEntry(candidate, info.LastWriteTimeUtc, info.Length, content);
            return content;
        }

        throw new FileNotFoundException($"未找到插件页面: {pluginId}/{pageKey}");
    }

    // ------------------------------------------------------------------ 注入与侧边栏

    public async Task<List<PluginInjection>> GetInjectionsAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        var result = new List<PluginInjection>(snapshot.Count);

        foreach (var plugin in snapshot)
        {
            if (!plugin.Config.Enabled)
            {
                continue;
            }

            var capabilities = plugin.Manifest.Capabilities;
            if (!capabilities.HasAnyInjection)
            {
                continue;
            }

            result.Add(new PluginInjection
            {
                PluginId = plugin.Manifest.Id,
                Css = capabilities.InjectStyles.Select(f => BuildFileUrl(plugin.Manifest.Id, f)).ToList(),
                Js = capabilities.InjectScripts.Select(f => BuildFileUrl(plugin.Manifest.Id, f)).ToList()
            });
        }

        return result;
    }

    /// <summary>兼容旧接口：返回匿名对象结构，字段名与旧版完全一致。</summary>
    public async Task<List<object>> GetInjectsAsync(CancellationToken ct = default)
    {
        var injections = await GetInjectionsAsync(ct).ConfigureAwait(false);
        return injections
            .Select(i => (object)new { pluginId = i.PluginId, css = i.Css, js = i.Js })
            .ToList();
    }

    public async Task<List<PluginSidebar>> GetSidebarsAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        var result = new List<PluginSidebar>();

        foreach (var plugin in snapshot)
        {
            if (!plugin.Config.Enabled || !plugin.Manifest.Capabilities.Sidebar)
            {
                continue;
            }

            result.Add(new PluginSidebar
            {
                Id = plugin.Manifest.Id,
                Name = plugin.Manifest.Name,
                Icon = plugin.Manifest.Icon,
                Items = plugin.Manifest.SidebarItems ?? new List<PluginSidebarItem>()
            });
        }

        return result;
    }

    public async Task<object> GetPluginConfigForApiAsync(string id, CancellationToken ct = default)
    {
        var plugin = await GetPluginForApiAsync(id, ct).ConfigureAwait(false);

        var defaults = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (plugin.Manifest.DefaultConfig is JsonObject dc)
        {
            foreach (var kv in dc)
            {
                defaults[kv.Key] = kv.Value;
            }
        }

        return new { settings = plugin.Config.Settings, defaults, schema = _validator.DescribeSettings(plugin.Manifest) };
    }

    // ------------------------------------------------------------------ 进程内插件与生命周期

    /// <summary>
    /// 注册首方进程内插件：它同样出现在插件列表里，并参与启用/禁用与生命周期回调，
    /// 但能力由注册方在 <see cref="IPluginLifecycle"/> 内部提供（例如注册扩展点）。
    /// </summary>
    public async Task<LoadedPlugin> RegisterInProcessPluginAsync(
        IPluginLifecycle plugin,
        PluginManifest? manifest = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var resolved = manifest ?? new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Id,
            Version = "1.0.0",
            Author = "DeEarthX",
            AutoStart = true
        };

        resolved.Id = string.IsNullOrWhiteSpace(resolved.Id) ? plugin.Id : resolved.Id;
        resolved.Normalize();

        var config = await _configStore.GetOrCreateAsync(resolved, PluginsDir, ct).ConfigureAwait(false);
        var context = new PluginContext(resolved, config, PluginsDir, _logService, _events, _extensions);

        var loaded = new LoadedPlugin
        {
            Manifest = resolved,
            Config = config,
            DirectoryPath = PluginsDir,
            Context = context,
            State = PluginLoadState.Ready
        };

        _plugins[resolved.Id] = loaded;
        _inProcessPlugins[resolved.Id] = plugin;

        await _configStore.SaveAsync(resolved.Id, config, ct).ConfigureAwait(false);

        _loadGate.Wait(ct);
        try
        {
            RebuildSnapshot();
        }
        finally
        {
            _loadGate.Release();
        }

        AdvanceVersion();

        try
        {
            await plugin.InitializeAsync(context, ct).ConfigureAwait(false);
            if (config.Enabled && config.AutoStart)
            {
                await plugin.StartAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"进程内插件初始化失败: {resolved.Id}", ex);
        }

        return loaded;
    }

    /// <summary>运行插件的生命周期脚本（manifest 的 <c>lifecycle.start</c> / <c>lifecycle.stop</c>）。</summary>
    public async Task RunLifecycleScriptAsync(LoadedPlugin plugin, bool stop, CancellationToken ct = default)
    {
        var relative = stop ? plugin.Manifest.Lifecycle?.Stop : plugin.Manifest.Lifecycle?.Start;
        if (string.IsNullOrWhiteSpace(relative))
        {
            return;
        }

        try
        {
            var path = PluginPaths.ResolveInside(plugin.DirectoryPath, relative);
            var payload = JsonSerializer.Serialize(new
            {
                hook = stop ? "lifecycle.stop" : "lifecycle.start",
                pluginId = plugin.Manifest.Id,
                pluginDirectory = plugin.DirectoryPath
            }, Json);

            await _events.PublishAsync(
                stop ? "PluginStopping" : "PluginStarting",
                new PluginEventArgs { Payload = plugin.Manifest.Id },
                ct).ConfigureAwait(false);

            // 生命周期脚本按“尽力而为”执行：失败只记录，不阻塞宿主启停。
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = $"\"{path}\"",
                        WorkingDirectory = plugin.DirectoryPath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process is null)
                    {
                        return;
                    }

                    process.StandardInput.Write(payload);
                    process.StandardInput.Close();
                    process.WaitForExit(15_000);
                }
                catch (Exception ex)
                {
                    _logService.Warn($"插件生命周期脚本执行失败: {plugin.Manifest.Id} ({ex.Message})");
                }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.Warn($"插件生命周期脚本解析失败: {plugin.Manifest.Id} ({ex.Message})");
        }
    }

    private async Task StartInProcessAsync(LoadedPlugin plugin, CancellationToken ct)
    {
        if (!_inProcessPlugins.TryGetValue(plugin.Manifest.Id, out var inProcess))
        {
            await RunLifecycleScriptAsync(plugin, stop: false, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await inProcess.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.Error($"进程内插件启动失败: {plugin.Manifest.Id}", ex);
        }
    }

    private async Task StopInProcessAsync(LoadedPlugin plugin, CancellationToken ct)
    {
        if (!_inProcessPlugins.TryGetValue(plugin.Manifest.Id, out var inProcess))
        {
            return;
        }

        try
        {
            await inProcess.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.Error($"进程内插件停止失败: {plugin.Manifest.Id}", ex);
        }
    }

    // ------------------------------------------------------------------ 热重载

    /// <summary>外部改动（用户手动编辑插件目录）后调用：丢弃缓存并重新扫描。</summary>
    public Task NotifyExternalChangeAsync(CancellationToken ct = default) => ReloadAllAsync(ct);

    public void EnableHotReload(bool enabled = true) => _hotReload = enabled;

    // ------------------------------------------------------------------ 内部

    private async Task<IReadOnlyList<LoadedPlugin>> GetSnapshotAsync(CancellationToken ct)
    {
        if (_snapshot is { } cached && !_disposed)
        {
            return cached;
        }

        var task = EnsureLoadStarted(ct);
        await task.ConfigureAwait(false);
        return (IReadOnlyList<LoadedPlugin>?)_snapshot ?? Array.Empty<LoadedPlugin>();
    }

    private Task<PluginLoadReport> EnsureLoadStarted(CancellationToken ct)
    {
        if (_loadTask is { } existing)
        {
            return existing;
        }

        _loadGate.Wait(ct);
        try
        {
            if (_loadTask is { } raced)
            {
                return raced;
            }

            _loadTask = LoadAllCoreAsync(ct);
            return _loadTask;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<PluginLoadReport> LoadAllCoreAsync(CancellationToken ct)
    {
        try
        {
            var factory = new PluginContextFactory(_logService, _events, _extensions);
            var report = await _loader.LoadAllAsync(factory, ct).ConfigureAwait(false);

            _loadGate.Wait(ct);
            try
            {
                _plugins.Clear();
                _lifecycleScripts.Clear();

                foreach (var plugin in report.Plugins)
                {
                    _plugins[plugin.Manifest.Id] = plugin;
                    RegisterLifecycleScripts(plugin);
                }

                // 失败目录也保留一份占位，便于诊断接口展示；但不进入可运行集合。
                foreach (var failure in report.Failures)
                {
                    _lifecycleScripts.TryRemove(failure.Id, out _);
                }

                _lastReport = report;
                RebuildSnapshot();
            }
            finally
            {
                _loadGate.Release();
            }

            AdvanceVersion();

            await StartEnabledPluginsAsync(report.Plugins, ct).ConfigureAwait(false);
            return report;
        }
        catch
        {
            // 失败后允许下一次调用重试，而不是永久缓存一个 faulted task。
            _loadGate.Wait(ct);
            try
            {
                _loadTask = null;
            }
            finally
            {
                _loadGate.Release();
            }

            throw;
        }
    }

    /// <summary>把单个插件目录装进内存，用于“创建/安装/重载单个插件”的增量路径。</summary>
    private async Task LoadIntoAsync(string pluginDir, CancellationToken ct)
    {
        var factory = new PluginContextFactory(_logService, _events, _extensions);
        var installed = _plugins
            .Where(kv => !string.Equals(kv.Value.DirectoryPath, pluginDir, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Manifest.Version, StringComparer.Ordinal);

        var plugin = await _loader.LoadSingleAsync(pluginDir, factory, installed, ct).ConfigureAwait(false);

        _plugins[plugin.Manifest.Id] = plugin;
        RegisterLifecycleScripts(plugin);

        _loadGate.Wait(ct);
        try
        {
            RebuildSnapshot();
        }
        finally
        {
            _loadGate.Release();
        }

        AdvanceVersion();

        if (plugin.State == PluginLoadState.Ready && plugin.Config.Enabled && plugin.Config.AutoStart)
        {
            await StartInProcessAsync(plugin, ct).ConfigureAwait(false);
            await RunLifecycleScriptAsync(plugin, stop: false, ct).ConfigureAwait(false);
        }
    }

    private void RegisterLifecycleScripts(LoadedPlugin plugin)
    {
        var lifecycle = plugin.Manifest.Lifecycle;
        if (lifecycle is null || (string.IsNullOrWhiteSpace(lifecycle.Start) && string.IsNullOrWhiteSpace(lifecycle.Stop)))
        {
            _lifecycleScripts.TryRemove(plugin.Manifest.Id, out _);
            return;
        }

        _lifecycleScripts[plugin.Manifest.Id] = lifecycle;
    }

    private async Task StartEnabledPluginsAsync(IReadOnlyList<LoadedPlugin> plugins, CancellationToken ct)
    {
        foreach (var plugin in plugins)
        {
            if (!plugin.Config.Enabled || !plugin.Config.AutoStart)
            {
                continue;
            }

            await StartInProcessAsync(plugin, ct).ConfigureAwait(false);
            await RunLifecycleScriptAsync(plugin, stop: false, ct).ConfigureAwait(false);
        }
    }

    private void RebuildSnapshot()
    {
        // 稳定排序：依赖拓扑序已经由 loader 保证，这里只按 id 做最终确定性排序，
        // 让 /plugins 的响应顺序在多次调用之间保持一致（旧实现顺序随机）。
        _snapshot = _plugins.Values
            .Where(p => p.State == PluginLoadState.Ready)
            .OrderBy(p => p.Manifest.Id, StringComparer.Ordinal)
            .ToList();
    }

    private void AdvanceVersion() => Interlocked.Increment(ref _version);

    private void WriteGlobalPluginConfigs()
    {
        // 兼容旧版：保留 <app>/plugin-configs.json 的 {"<id>": "true"} 形式，
        // 使降级到旧版本时启用状态不丢失。新版本自身读取的是 PluginConfigStore 的富格式。
        try
        {
            var configs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in _plugins)
            {
                configs[kv.Key] = kv.Value.Config.Enabled.ToString().ToLowerInvariant();
            }

            var directory = Path.GetDirectoryName(GlobalConfigsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(GlobalConfigsPath, JsonSerializer.Serialize(configs, Json));
        }
        catch (Exception ex)
        {
            _logService.Error("保存全局插件配置失败", ex);
        }
    }

    private async Task PublishInstalledAsync(LoadedPlugin plugin, CancellationToken ct)
    {
        var hookContext = new HookContext(
            nameof(PluginHook.PluginInstalled),
            null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pluginId"] = plugin.Manifest.Id,
                ["pluginName"] = plugin.Manifest.Name,
                ["pluginVersion"] = plugin.Manifest.Version
            });

        await _events.PublishAsync(
            nameof(PluginHook.PluginInstalled),
            new PluginEventArgs { Payload = plugin.Manifest, Meta = hookContext.Meta },
            ct).ConfigureAwait(false);
    }

    private async Task PublishStateChangedAsync(LoadedPlugin plugin, bool enabled, CancellationToken ct)
    {
        await _events.PublishAsync(
            nameof(PluginHook.PluginStateChanged),
            new PluginEventArgs
            {
                Payload = new { pluginId = plugin.Manifest.Id, enabled },
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["pluginId"] = plugin.Manifest.Id,
                    ["enabled"] = enabled
                }
            },
            ct).ConfigureAwait(false);
    }

    private void InvalidatePageCache(string pluginId)
    {
        foreach (var key in _pageCache.Keys.Where(k => k.StartsWith(pluginId + "/", StringComparison.Ordinal)))
        {
            _pageCache.TryRemove(key, out _);
        }
    }

    private async Task WriteManifestAsync(string dir, PluginManifest manifest, CancellationToken ct)
    {
        var path = Path.Combine(dir, ManifestFileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, Json), ct).ConfigureAwait(false);
    }

    private async Task<PluginManifest> ReadManifestFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Json)
            ?? throw new InvalidDataException("manifest.json 解析失败");
        return manifest.Normalize();
    }

    private static string BuildPluginId()
    {
        // 10 位数字 id：与前端“自动生成 10 位数字 ID”的说明保持一致，且便于人工朗读。
        Span<char> buffer = stackalloc char[10];
        var value = Guid.NewGuid().ToByteArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (char)('0' + value[i] % 10);
        }

        return new string(buffer);
    }

    /// <summary>只保留字母、数字、下划线、短横线与点，避免 id 被用于路径拼接时越权。</summary>
    internal static string SanitizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Guid.NewGuid().ToString("N")[..10];
        }

        var builder = new StringBuilder(id.Length);
        foreach (var ch in id)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                builder.Append(ch);
            }
        }

        var result = builder.ToString().Trim('.', '-', '_');
        return string.IsNullOrEmpty(result) ? Guid.NewGuid().ToString("N")[..10] : result;
    }

    private static string BuildFileUrl(string pluginId, string relativePath)
        => $"/plugins/{pluginId}/files/{relativePath.Replace('\\', '/').TrimStart('/')}";

    private static string? DetectTopLevelDirectory(byte[] zipBytes)
    {
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            string? topLevel = null;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var slash = entry.FullName.IndexOf('/');
                var current = slash >= 0 ? entry.FullName[..slash] : null;

                if (topLevel is null)
                {
                    topLevel = current;
                }
                else if (!string.Equals(topLevel, current, StringComparison.Ordinal))
                {
                    // 存在多个顶层条目（或文件直接在根），说明包内就是插件根目录。
                    return null;
                }
            }

            return topLevel;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(file, target, true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // 目录被占用时留给后续清理，不影响主流程。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // ignore
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadGate.Dispose();
        _mutationGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record PageCacheEntry(string Path, DateTime LastWriteUtc, long Length, string Content);
}

public sealed record PluginInstallResult(string? PluginId, string? Error, bool RequirePassword);

public sealed class PluginInjection
{
    public string PluginId { get; set; } = string.Empty;
    public List<string> Css { get; set; } = new();
    public List<string> Js { get; set; } = new();
}

public sealed class PluginSidebar
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public List<PluginSidebarItem> Items { get; set; } = new();
}

public sealed class PluginDiagnostics
{
    public int Total { get; set; }
    public int Enabled { get; set; }
    public long LoadedInMilliseconds { get; set; }
    public bool HotReload { get; set; }
    public List<PluginDiagnosticsEntry> Plugins { get; set; } = new();
    public List<PluginDiagnosticsEntry> Failures { get; set; } = new();
    public List<PluginExtensionPointInfo> ExtensionPoints { get; set; } = new();
    public IReadOnlyList<string> DeclaredHooks { get; set; } = Array.Empty<string>();
}

public sealed class PluginDiagnosticsEntry
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Directory { get; set; }
    public string State { get; set; } = "Ready";
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; }
    public string? Error { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public IReadOnlyList<string> ResolvedDependencies { get; set; } = Array.Empty<string>();
    public List<string> Hooks { get; set; } = new();
}
