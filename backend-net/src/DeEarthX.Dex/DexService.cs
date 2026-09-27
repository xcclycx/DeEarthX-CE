using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Configuration;
using DeEarthX.Dearth;
using DeEarthX.Dex.Optimization;
using DeEarthX.Infrastructure.Process;
using DeEarthX.Infrastructure.Zip;
using DeEarthX.ModLoader;
using DeEarthX.Platform;
using DeEarthX.Plugins;
using DeEarthX.Realtime;

namespace DeEarthX.Dex;

public sealed class DexService
{
    private readonly PlatformService _platformService;
    private readonly IModLoaderService _modLoaderService;
    private readonly ModFilterService _modFilterService;
    private readonly PluginManager _pluginManager;
    private readonly IPluginHookExecutor _hookExecutor;
    private readonly PluginFilterStrategyProvider _filterStrategyProvider;
    private readonly IMessageService _messageService;
    private readonly IConfigService _configService;
    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly IZipService _zipService;
    private readonly ILogService _logService;
    private readonly IProcessService _processService;
    private readonly ExportOptimizer _exportOptimizer;

    private static readonly string[] ManifestFiles = { "manifest.json", "modrinth.index.json" };

    private static readonly string[] OverrideBlacklist =
    {
        "overrides/options.txt",
        "overrides/shaderpacks",
        "overrides/essential",
        "overrides/resourcepacks",
        "overrides/PCL",
        "overrides/CustomSkinLoader"
    };

    public DexService(
        PlatformService platformService,
        IModLoaderService modLoaderService,
        ModFilterService modFilterService,
        PluginManager pluginManager,
        IPluginHookExecutor hookExecutor,
        PluginFilterStrategyProvider filterStrategyProvider,
        IMessageService messageService,
        IConfigService configService,
        IAppDirectoryProvider appDirectoryProvider,
        IZipService zipService,
        ILogService logService,
        IProcessService processService,
        ExportOptimizer exportOptimizer)
    {
        _platformService = platformService;
        _modLoaderService = modLoaderService;
        _modFilterService = modFilterService;
        _pluginManager = pluginManager;
        _hookExecutor = hookExecutor;
        _filterStrategyProvider = filterStrategyProvider;
        _messageService = messageService;
        _configService = configService;
        _appDirectoryProvider = appDirectoryProvider;
        _zipService = zipService;
        _logService = logService;
        _processService = processService;
        _exportOptimizer = exportOptimizer;
    }

    public async Task MainAsync(byte[] buffer, bool isServerMode, string? filename = null, string? template = null, CancellationToken ct = default)
    {
        var startTime = Stopwatch.GetTimestamp();
        try
        {
            await ProcessModpackAsync(buffer, filename, startTime, isServerMode, template, ct);
        }
        catch (Exception ex)
        {
            _logService.Error("主流程执行失败", ex);
            await NotifyFailureAsync(filename, ex, ct);
            await _messageService.HandleError(ex.Message);
        }
    }

    /// <summary>
    /// 把失败告知插件。插件可以据此上报、清理或做本地兜底；
    /// 这里吞掉插件自身的异常，保证报错流程本身不会再抛出。
    /// </summary>
    private async Task NotifyFailureAsync(string? filename, Exception ex, CancellationToken ct)
    {
        try
        {
            var meta = new Dictionary<string, object?>
            {
                ["modpackName"] = filename ?? "unknown",
                ["error"] = ex.Message,
                ["errorType"] = ex.GetType().Name
            };

            await _hookExecutor.ExecuteAsync(PluginHook.OnError, new HookContext("onError", null, meta), ct);
        }
        catch (Exception hookEx)
        {
            _logService.Warn($"插件错误钩子执行失败: {hookEx.Message}");
        }
    }

    private async Task ProcessModpackAsync(byte[] buffer, string? filename, long startTimeTicks, bool isServerMode, string? template, CancellationToken ct)
    {
        var hookMeta = new Dictionary<string, object?>
        {
            ["modpackName"] = filename ?? "unknown",
            ["serverMode"] = isServerMode,
            ["template"] = template
        };

        var beforeCtx = new HookContext("beforeModpackProcess", buffer, hookMeta);
        var beforeResult = await _hookExecutor.ExecuteAsync(PluginHook.BeforeModpackProcess, beforeCtx, ct);
        buffer = beforeResult.Data ?? buffer;

        var processedBuffer = await ProcessModpackExtractAsync_Builder(buffer, filename, ct);

        var afterCtx = new HookContext("afterModpackProcess", processedBuffer, hookMeta);
        var afterResult = await _hookExecutor.ExecuteAsync(PluginHook.AfterModpackProcess, afterCtx, ct);
        if (afterResult.Data is not null)
        {
            processedBuffer = afterResult.Data;
            hookMeta["buffer"] = processedBuffer;
        }

        var platformType = _platformService.Detect(processedBuffer);
        _logService.Info($"检测到平台: {platformType}");

        var manifest = _platformService.ReadManifest(processedBuffer, platformType);
        if (manifest is null)
        {
            _logService.Error("整合包信息为空", null);
            await _messageService.HandleError("该整合包似乎不是有效的整合包。");
            return;
        }

        var mpname = manifest["name"]?.GetValue<string>() ?? (filename ?? "unknown");
        var instanceRoot = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "instance");
        var unpath = Path.Combine(instanceRoot, mpname);

        await ParallelTasksAsync(processedBuffer, mpname, platformType, manifest, unpath, ct);
        await _messageService.StatusChange();

        hookMeta["filePath"] = unpath;
        await _hookExecutor.ExecuteAsync(PluginHook.BeforeFilterMods, new HookContext("beforeFilterMods", null, hookMeta), ct);
        await FilterModsAsync(unpath, mpname, ct);
        await _messageService.StatusChange();
        await _hookExecutor.ExecuteAsync(PluginHook.AfterFilterMods, new HookContext("afterFilterMods", null, hookMeta), ct);

        await _hookExecutor.ExecuteAsync(PluginHook.BeforeInstallLoader, new HookContext("beforeInstallLoader", null, hookMeta), ct);
        await InstallModLoaderAsyncSafeAsync(manifest, platformType, unpath, isServerMode, template, ct);
        await _hookExecutor.ExecuteAsync(PluginHook.AfterInstallLoader, new HookContext("afterInstallLoader", null, hookMeta), ct);

        await _messageService.StatusChange();
        var optimizationReport = await OptimizeExportAsync(unpath, mpname, ct);
        hookMeta["optimizationReport"] = optimizationReport;
        await _hookExecutor.ExecuteAsync(PluginHook.OnOutputZip, new HookContext("onOutputZip", null, hookMeta), ct);

        await CompleteTaskAsync(startTimeTicks, unpath, mpname, isServerMode, ct);
    }

    /// <summary>
    /// 安装模组加载器服务端，并把这一步的失败降级为「警告 + 继续」。
    ///
    /// 这一步依赖外网（Forge/Fabric/NeoForge 元数据接口）。网络不可用时，
    /// 已经解压好的整合包内容仍然值得保留：继续跑完资源剥离、启动脚本与
    /// 优化报告，用户至少能拿到一个优化过的目录，并在报告里看到失败原因。
    /// </summary>
    private async Task InstallModLoaderAsyncSafeAsync(
        JsonObject manifest, PlatformType platformType, string unpath, bool isServerMode, string? template, CancellationToken ct)
    {
        try
        {
            await InstallModLoaderAsync(manifest, platformType, unpath, isServerMode, template, ct);
            _lastLoaderError = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _lastLoaderError = ex.Message;
            _logService.Error("模组加载器安装失败，继续执行导出优化", ex);
            await _messageService.ServerInstallError(ex.Message, "install-loader");
            await _messageService.Info($"模组加载器安装失败，已跳过并继续优化：{ex.Message}");
        }
    }

    /// <summary>最近一次模组加载器安装的失败原因，供优化报告展示。</summary>
    private string? _lastLoaderError;

    /// <summary>
    /// 运行导出优化流水线：Aikar's Flags、启动脚本、客户端资源剥离、
    /// 敏感信息清理、冗余检测、server.properties 生成与优化报告。
    ///
    /// 整条流水线都是非破坏性的：被移除的内容进入 backup/，并且支持 dry-run。
    /// </summary>
    private async Task<OptimizationReport?> OptimizeExportAsync(string unpath, string mpname, CancellationToken ct)
    {
        var config = _configService.Get();
        var optimization = config.Optimization ?? OptimizationConfig.CreateDefault();

        if (!optimization.Enabled)
        {
            _logService.Info("导出优化已在设置中关闭，跳过优化流水线");
            return null;
        }

        try
        {
            var report = await _exportOptimizer.OptimizeAsync(unpath, mpname, optimization, TargetPlatform.Both, ct);

            // 若加载器安装失败，把它作为一条显式建议写进报告，
            // 避免用户以为导出的目录已经可以直接开服。
            if (!string.IsNullOrEmpty(_lastLoaderError))
            {
                report.Suggestions.Insert(0,
                    $"模组加载器服务端未能安装（{_lastLoaderError}）。请在网络可用时重新制作，或手动放置服务端核心后再启动。");
                report.Warnings.Add($"模组加载器安装失败: {_lastLoaderError}");
            }

            await PersistReportAsync(unpath, report, ct);
            await _messageService.OptimizationReport(report);
            return report;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 优化失败绝不能影响服务端导出本身。
            _logService.Error("导出优化流水线失败，已跳过优化继续导出", ex);
            await _messageService.Info($"优化流水线失败，已跳过：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把优化报告写到服务端目录，方便用户直接查阅与排查。
    /// </summary>
    private async Task PersistReportAsync(string unpath, OptimizationReport report, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(unpath, "deearthx-optimization-report.json");
            var json = JsonSerializer.Serialize(report, DeEarthXJsonOptions.Default);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex)
        {
            _logService.Warn($"写入优化报告失败: {ex.Message}");
        }
    }

    private async Task ParallelTasksAsync(byte[] buffer, string mpname, PlatformType platformType, JsonObject manifest, string unpath, CancellationToken ct)
    {
        var unzipTask = UnzipOverridesAsync(buffer, mpname, ct);
        var downloadTask = _platformService.DownloadFilesAsync(manifest, platformType, unpath, _messageService, ct);

        try
        {
            await Task.WhenAll(unzipTask, downloadTask);
        }
        catch (Exception ex)
        {
            _logService.Error("并行任务执行异常", ex);
        }
    }

    private async Task<byte[]> ProcessModpackExtractAsync_Builder(byte[] buffer, string? filename, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filename) || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return buffer;
        }

        try
        {
            using var ms = new MemoryStream(buffer);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var mrpackEntry = archive.Entries.FirstOrDefault(e => e.FullName == "modpack.mrpack");
            if (mrpackEntry is null)
            {
                return buffer;
            }

            _logService.Info("检测到 PCL 整合包格式，提取 modpack.mrpack");
            using var es = mrpackEntry.Open();
            using var outMs = new MemoryStream();
            await es.CopyToAsync(outMs, ct);
            return outMs.ToArray();
        }
        catch (Exception ex)
        {
            _logService.Error("处理整合包失败，使用原始缓冲区", ex);
            return buffer;
        }
    }

    private async Task UnzipOverridesAsync(byte[] buffer, string mpname, CancellationToken ct)
    {
        var instancePath = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "instance", mpname);
        Directory.CreateDirectory(instancePath);

        var entries = _zipService.ReadEntries(buffer);
        var total = entries.Count;
        var index = 0;

        using var ms = new MemoryStream(buffer);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            index++;
            ct.ThrowIfCancellationRequested();
            var fullName = entry.FullName;

            await _messageService.Unzip(fullName, total, index);

            if (!fullName.StartsWith("overrides/"))
            {
                continue;
            }

            if (IsBlacklisted(fullName))
            {
                continue;
            }

            var relative = fullName.Substring("overrides/".Length);
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var targetPath = Path.Combine(instancePath, relative.Replace('/', Path.DirectorySeparatorChar));
            var dirName = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dirName))
            {
                Directory.CreateDirectory(dirName);
            }

            if (entry.Length == 0 && (fullName.EndsWith('/') || fullName.EndsWith('\\')))
            {
                continue;
            }

            if (File.Exists(targetPath))
            {
                continue;
            }

            entry.ExtractToFile(targetPath, false);
        }

        _logService.Info($"解压流程完成: {mpname}, 总文件数: {total}");
    }

    private static bool IsBlacklisted(string fullName)
    {
        if (fullName == "overrides/" || fullName == "overrides")
        {
            return true;
        }

        foreach (var item in OverrideBlacklist)
        {
            var normalizedItem = item.EndsWith('/') ? item : item + "/";
            var normalizedFile = fullName.EndsWith('/') ? fullName : fullName + "/";
            if (normalizedFile == normalizedItem || normalizedFile.StartsWith(normalizedItem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task FilterModsAsync(string unpath, string mpname, CancellationToken ct)
    {
        var config = _configService.Get();
        var modsDir = Path.Combine(unpath, "mods");
        var started = Stopwatch.GetTimestamp();
        var extraStrategies = await _filterStrategyProvider.GetFilterStrategiesAsync(ct);

        if (extraStrategies.Count > 0)
        {
            _logService.Info($"已加载 {extraStrategies.Count} 条插件过滤策略: {string.Join(", ", extraStrategies.Select(s => s.Name))}");
        }

        await _modFilterService.FilterAsync(modsDir, mpname, config, extraStrategies, _messageService, ct);

        if (extraStrategies.Count > 0)
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _logService.Info($"插件的过滤策略参与完成，总耗时 {elapsed}ms");
        }
    }

    private async Task InstallModLoaderAsync(JsonObject manifest, PlatformType platformType, string unpath, bool isServerMode, string? template, CancellationToken ct)
    {
        var modpackInfo = _platformService.GetInfo(manifest, platformType);

        if (isServerMode)
        {
            await _modLoaderService.MlSetupAsync(modpackInfo.Loader, modpackInfo.Minecraft, modpackInfo.LoaderVersion, unpath, _messageService, template, ct);
        }
        else
        {
            await _modLoaderService.DInstallAsync(modpackInfo.Loader, modpackInfo.Minecraft, modpackInfo.LoaderVersion, unpath, ct);
        }
    }

    private async Task CompleteTaskAsync(long startTimeTicks, string unpath, string mpname, bool isServerMode, CancellationToken ct)
    {
        var config = _configService.Get();
        var elapsedMs = Stopwatch.GetElapsedTime(startTimeTicks).TotalMilliseconds;
        var duration = (long)elapsedMs;

        if (isServerMode)
        {
            await _messageService.ServerInstallComplete(unpath, duration);
        }
        else
        {
            await _messageService.Finish(0, duration);
        }

        if (!isServerMode && config.AutoZip)
        {
            await CreateOutputZipAsync(unpath, mpname, ct);
        }

        if (config.Oaf)
        {
            var instanceDir = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "instance");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo("explorer.exe", instanceDir) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _logService.Error("打开 instance 目录失败", ex);
            }
        }

        _logService.Info($"任务完成，耗时 {duration}ms");
    }

    private async Task CreateOutputZipAsync(string sourcePath, string mpname, CancellationToken ct)
    {
        var outputPath = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "instance", $"{mpname}.zip");
        await _zipService.CreateZipAsync(sourcePath, outputPath, 9, ct);
        await _messageService.Info($"服务端已打包: {mpname}.zip");
    }
}
