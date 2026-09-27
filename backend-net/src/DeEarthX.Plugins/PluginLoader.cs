using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

/// <summary>一次加载扫描的完整结果。</summary>
public sealed record PluginLoadReport(
    IReadOnlyList<LoadedPlugin> Plugins,
    IReadOnlyList<PluginLoadFailure> Failures,
    long ElapsedMilliseconds)
{
    public int Total => Plugins.Count + Failures.Count;

    public static PluginLoadReport Empty { get; } = new(Array.Empty<LoadedPlugin>(), Array.Empty<PluginLoadFailure>(), 0);
}

/// <summary>加载失败的插件目录，仍然出现在诊断接口里，便于用户定位问题。</summary>
public sealed record PluginLoadFailure(
    string Id,
    string DirectoryPath,
    PluginLoadState State,
    string Error);

public interface IPluginLoader
{
    Task<PluginLoadReport> LoadAllAsync(PluginContextFactory contextFactory, CancellationToken ct = default);

    /// <summary>
    /// 只装载一个插件目录（创建 / 安装 / 重载单个插件时使用），
    /// 避免为了一个插件重扫整个 plugins 目录。
    /// 依赖关系仍会被解析，但依赖项按“已装插件”集合判定。
    /// </summary>
    Task<LoadedPlugin> LoadSingleAsync(
        string pluginDirectory,
        PluginContextFactory contextFactory,
        IReadOnlyDictionary<string, string> installedVersions,
        CancellationToken ct = default);
}

/// <summary>
/// 把插件目录（含清单、配置、依赖）装配成可运行的 <see cref="LoadedPlugin"/> 集合。
/// 只负责“读”，不负责“跑”——生命周期与钩子由 <see cref="PluginManager"/> 与钩子执行器负责。
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private const string ManifestFileName = "manifest.json";

    /// <summary>并行读取清单的并发上限：磁盘随机读并发过高反而更慢。</summary>
    private const int MaxScanConcurrency = 8;

    private readonly string _pluginsDir;
    private readonly IPluginConfigStore _configStore;
    private readonly PluginValidator _validator;
    private readonly ILogService _log;
    private readonly JsonSerializerOptions _json = DeEarthXJsonOptions.Default;

    public PluginLoader(
        IAppDirectoryProvider appDirectoryProvider,
        IPluginConfigStore configStore,
        PluginValidator validator,
        ILogService log)
    {
        _pluginsDir = Path.Combine(appDirectoryProvider.GetAppDirectory(), "plugins");
        _configStore = configStore;
        _validator = validator;
        _log = log;
    }

    public async Task<PluginLoadReport> LoadAllAsync(PluginContextFactory contextFactory, CancellationToken ct = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!Directory.Exists(_pluginsDir))
        {
            return PluginLoadReport.Empty;
        }

        // 一次枚举取得全部候选目录，避免多次 syscall；下划线开头的目录是安装/解压临时目录。
        var candidates = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(_pluginsDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('_') || name.StartsWith('.'))
            {
                continue;
            }

            if (File.Exists(Path.Combine(dir, ManifestFileName)))
            {
                candidates.Add(dir);
            }
        }

        var failures = new List<PluginLoadFailure>();
        var staged = new List<StagedPlugin>(candidates.Count);

        // 并行读取清单 + 配置：目录数量多时（几十个插件）相比串行有数量级提升。
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Min(MaxScanConcurrency, Math.Max(1, Environment.ProcessorCount))
            },
            async (dir, token) =>
            {
                try
                {
                    var plugin = await ReadOneAsync(dir, contextFactory, token).ConfigureAwait(false);
                    if (plugin is null)
                    {
                        return;
                    }

                    lock (staged)
                    {
                        staged.Add(plugin);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var id = Path.GetFileName(dir);
                    _log.Error($"加载插件失败: {dir}", ex);
                    lock (failures)
                    {
                        failures.Add(new PluginLoadFailure(id, dir, PluginLoadState.Failed, ex.Message));
                    }
                }
            }).ConfigureAwait(false);

        var (plugins, orderingFailures) = ApplyDependencyOrder(staged);
        failures.AddRange(orderingFailures);

        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _log.Info($"插件扫描完成: 成功 {plugins.Count} 个，失败 {failures.Count} 个，耗时 {elapsed}ms");

        return new PluginLoadReport(plugins, failures, elapsed);
    }

    public async Task<LoadedPlugin> LoadSingleAsync(
        string pluginDirectory,
        PluginContextFactory contextFactory,
        IReadOnlyDictionary<string, string> installedVersions,
        CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(pluginDirectory);
        var staged = await ReadOneAsync(dir, contextFactory, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException($"插件加载失败: {dir}");

        var plugin = staged.Plugin;

        var missing = new List<string>();
        var satisfied = new List<string>();

        foreach (var dependency in plugin.Manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Id) || dependency.Optional)
            {
                satisfied.Add(dependency.Id);
                continue;
            }

            if (!installedVersions.TryGetValue(dependency.Id, out var installedVersion))
            {
                missing.Add($"缺少依赖插件: {dependency.Id}");
                continue;
            }

            if (!VersionSatisfies(installedVersion, dependency.Version, out var error))
            {
                missing.Add($"依赖 {dependency.Id} 版本不满足（要求 {dependency.Version}，实际 {installedVersion}）: {error}");
                continue;
            }

            satisfied.Add(dependency.Id);
        }

        if (missing.Count > 0)
        {
            return new LoadedPlugin
            {
                Manifest = plugin.Manifest,
                Config = plugin.Config,
                DirectoryPath = plugin.DirectoryPath,
                Context = plugin.Context,
                State = PluginLoadState.DependencyMissing,
                LoadError = string.Join("; ", missing),
                ResolvedDependencies = satisfied
            };
        }

        return new LoadedPlugin
        {
            Manifest = plugin.Manifest,
            Config = plugin.Config,
            DirectoryPath = plugin.DirectoryPath,
            Context = plugin.Context,
            State = plugin.State,
            LoadError = plugin.LoadError,
            ResolvedDependencies = satisfied
        };
    }

    private async Task<StagedPlugin?> ReadOneAsync(string dir, PluginContextFactory contextFactory, CancellationToken ct)
    {
        var manifestPath = Path.Combine(dir, ManifestFileName);
        var manifest = await ReadManifestAsync(manifestPath, ct).ConfigureAwait(false);

        // 目录名是 id 的兜底来源：清单缺失 id 时保持旧的兼容行为。
        var dirName = Path.GetFileName(dir);
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            manifest.Id = dirName;
        }

        var validation = _validator.Validate(manifest);
        if (!validation.IsValid)
        {
            _log.Warn($"插件清单校验未通过: {manifest.Id} -> {string.Join("; ", validation.Errors)}");
        }

        var config = await _configStore.GetOrCreateAsync(manifest, dir, ct).ConfigureAwait(false);
        var context = contextFactory.Create(manifest, config, dir);

        var plugin = new LoadedPlugin
        {
            Manifest = manifest,
            Config = config,
            DirectoryPath = dir,
            Context = context,
            State = validation.IsValid ? PluginLoadState.Ready : PluginLoadState.Invalid,
            LoadError = validation.IsValid ? null : string.Join("; ", validation.Errors)
        };

        return new StagedPlugin(plugin);
    }

    /// <summary>
    /// 依赖拓扑排序：被依赖者排在前面，保证钩子按正确顺序执行、扩展先于消费者注册。
    /// 缺失或成环的插件会被单独剔除并记录原因，而不是让整次加载失败。
    /// </summary>
    private (List<LoadedPlugin> Plugins, List<PluginLoadFailure> Failures) ApplyDependencyOrder(List<StagedPlugin> staged)
    {
        var failures = new List<PluginLoadFailure>();

        if (staged.Count == 0)
        {
            return (new List<LoadedPlugin>(), failures);
        }

        // 同名 id 只保留先扫到的那个，避免后续字典操作抛重复键异常。
        var byId = new Dictionary<string, LoadedPlugin>(StringComparer.Ordinal);
        foreach (var item in staged)
        {
            var id = item.Plugin.Manifest.Id;
            if (byId.TryAdd(id, item.Plugin))
            {
                continue;
            }

            _log.Warn($"发现重复的插件 ID，已跳过: {id} ({item.Plugin.DirectoryPath})");
        }

        var required = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dependent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (id, plugin) in byId)
        {
            required[id] = new List<string>();
            dependent.TryAdd(id, new List<string>());
            inDegree.TryAdd(id, 0);
        }

        foreach (var (id, plugin) in byId)
        {
            foreach (var dependency in plugin.Manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.Id))
                {
                    continue;
                }

                if (!byId.TryGetValue(dependency.Id, out var target))
                {
                    if (dependency.Optional)
                    {
                        _log.Debug($"插件 {id} 的可选依赖 {dependency.Id} 未安装，已忽略");
                        continue;
                    }

                    failures.Add(new PluginLoadFailure(
                        id,
                        plugin.DirectoryPath,
                        PluginLoadState.DependencyMissing,
                        $"缺少依赖插件: {dependency.Id}"));
                    continue;
                }

                if (!VersionSatisfies(target.Manifest.Version, dependency.Version, out var versionError))
                {
                    failures.Add(new PluginLoadFailure(
                        id,
                        plugin.DirectoryPath,
                        PluginLoadState.DependencyVersionMismatch,
                        $"依赖 {dependency.Id} 版本不满足（要求 {dependency.Version}，实际 {target.Manifest.Version}）: {versionError}"));
                    continue;
                }

                if (required[id].Contains(dependency.Id, StringComparer.Ordinal))
                {
                    continue;
                }

                required[id].Add(dependency.Id);
                dependent[dependency.Id].Add(id);
                inDegree[id] += 1;
            }
        }

        // 只要有未满足的硬依赖，就把它连同其下游一起剔除（下游不可能在缺少前置的情况下正确运行）。
        var blocked = new HashSet<string>(
            failures.Where(f => byId.ContainsKey(f.Id)).Select(f => f.Id),
            StringComparer.Ordinal);

        var directBlocked = new HashSet<string>(blocked, StringComparer.Ordinal);
        var pending = new Queue<string>(directBlocked);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!dependent.TryGetValue(id, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (blocked.Add(child))
                {
                    failures.Add(new PluginLoadFailure(
                        child,
                        byId[child].DirectoryPath,
                        PluginLoadState.DependencyMissing,
                        $"依赖未满足（前置插件 {id} 不可用）"));
                    pending.Enqueue(child);
                }
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0 && !blocked.Contains(kv.Key)).Select(kv => kv.Key));
        var ordered = new List<LoadedPlugin>(byId.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!emitted.Add(id))
            {
                continue;
            }

            ordered.Add(byId[id]);

            foreach (var child in dependent[id])
            {
                if (--inDegree[child] == 0 && !blocked.Contains(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        // 既没被发出、也没被依赖阻断的节点，只能处于环中。
        var orderedIds = new HashSet<string>(ordered.Select(p => p.Manifest.Id), StringComparer.Ordinal);
        foreach (var (id, plugin) in byId)
        {
            if (orderedIds.Contains(id) || blocked.Contains(id))
            {
                continue;
            }

            blocked.Add(id);
            failures.Add(new PluginLoadFailure(
                id,
                plugin.DirectoryPath,
                PluginLoadState.DependencyCycle,
                "插件依赖成环，已跳过加载"));
        }

        // 把解析出的依赖顺序回填给插件对象，供诊断与重载使用。
        var resolved = new List<LoadedPlugin>(ordered.Count);
        foreach (var plugin in ordered)
        {
            var id = plugin.Manifest.Id;
            var chain = required.TryGetValue(id, out var deps) ? deps : new List<string>();
            var failure = failures.FirstOrDefault(f => f.Id == id);

            resolved.Add(new LoadedPlugin
            {
                Manifest = plugin.Manifest,
                Config = plugin.Config,
                DirectoryPath = plugin.DirectoryPath,
                Context = plugin.Context,
                State = failure is null ? plugin.State : failure.State,
                LoadError = failure?.Error ?? plugin.LoadError,
                ResolvedDependencies = chain
            });
        }

        return (resolved, failures);
    }

    /// <summary>极简版本约束：支持 <c>*</c>、<c>&gt;=</c>、<c>&gt;</c>、<c>&lt;=</c>、<c>&lt;</c>、<c>=</c> 与裸版本号（视为 &gt;=）。</summary>
    private static bool VersionSatisfies(string? actual, string? requirement, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requirement) || requirement == "*")
        {
            return true;
        }

        if (!Version.TryParse(NormalizeVersion(actual), out var actualVersion))
        {
            error = $"无法解析插件版本 '{actual}'";
            return false;
        }

        var text = requirement.Trim();
        var op = ">=";
        foreach (var candidate in new[] { ">=", "<=", "==", ">", "<", "=" })
        {
            if (text.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                text = text[candidate.Length..].Trim();
                break;
            }
        }

        if (!Version.TryParse(NormalizeVersion(text), out var requiredVersion))
        {
            error = $"无法解析版本要求 '{requirement}'";
            return false;
        }

        var comparison = actualVersion.CompareTo(requiredVersion);
        var satisfied = op switch
        {
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            "=" or "==" => comparison == 0,
            _ => comparison >= 0
        };

        if (!satisfied)
        {
            error = $"需要 {op}{requiredVersion}";
        }

        return satisfied;
    }

    /// <summary>把 "1" / "1.2" 之类的短版本补成 <see cref="Version"/> 可解析的形式。</summary>
    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0";
        }

        var trimmed = version.Trim().TrimStart('v', 'V');
        var plus = trimmed.IndexOfAny(new[] { '+', '-' });
        if (plus > 0)
        {
            trimmed = trimmed[..plus];
        }

        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "0.0",
            1 => $"{parts[0]}.0",
            2 => trimmed,
            _ => $"{parts[0]}.{parts[1]}.{parts[2]}"
        };
    }

    private async Task<PluginManifest> ReadManifestAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, _json)
            ?? throw new InvalidDataException("manifest.json 解析失败");
        return manifest.Normalize();
    }

    private sealed record StagedPlugin(LoadedPlugin Plugin);
}

/// <summary>按插件创建运行上下文的工厂，避免 loader 直接依赖日志/事件/扩展注册中心。</summary>
public sealed class PluginContextFactory
{
    private readonly ILogService _log;
    private readonly IPluginEventBus _events;
    private readonly PluginExtensionCatalog _extensions;

    public PluginContextFactory(ILogService log, IPluginEventBus events, PluginExtensionCatalog extensions)
    {
        _log = log;
        _events = events;
        _extensions = extensions;
    }

    public PluginContext Create(PluginManifest manifest, PluginConfig config, string directoryPath)
        => new(manifest, config, directoryPath, _log, _events, _extensions);
}
