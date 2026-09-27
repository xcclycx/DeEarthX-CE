using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Configuration;
using DeEarthX.Realtime;

namespace DeEarthX.Dex.Optimization;

/// <summary>
/// 服务端导出优化器。
///
/// 安全约定：
/// <list type="number">
///   <item>任何修改之前都会把受影响的文件复制到 <c>backup/</c>（可通过配置关闭，但默认开启）。</item>
///   <item>非破坏性：被移除的模组移入 <c>disabled_mods/</c>，被移除的配置移入 <c>backup/</c>，而不是直接删除。</item>
///   <item>dry-run 模式下只分析、只生成报告，绝不写盘。</item>
///   <item>每一步操作都写入报告日志，包含被修改的文件路径与修改前后的值。</item>
/// </list>
/// </summary>
public sealed class ExportOptimizer
{
    private readonly ILogService _log;
    private readonly IMessageService _messageService;

    /// <summary>需要剥离的客户端专用目录（相对于服务端根目录）。</summary>
    private static readonly string[] ClientResourceDirectories =
    {
        "resourcepacks",
        "shaderpacks",
        "saves",
        "logs",
        "crash-reports",
        "screenshots",
        "journeymap",
        "replay_recordings",
        "localconfigs",
        "essential",
        "PCL",
        "CustomSkinLoader"
    };

    /// <summary>需要清理的敏感信息文件（相对路径或顶层文件名）。</summary>
    private static readonly string[] SensitiveFiles =
    {
        "options.txt",
        "optionsof.txt",
        "optionsshaders.txt",
        "servers.dat",
        "servers.dat_old",
        "usercache.json",
        "usernamecache.json",
        "realms_persistence.json",
        "launcher_profiles.json",
        "launcher_accounts.json",
        "launcher_accounts_microsoft_store.json",
        "banned-ips.json",
        "banned-players.json",
        "ops.json",
        "whitelist.json"
    };

    /// <summary>敏感文件所在的前缀目录（在这些目录下按文件名匹配）。</summary>
    private static readonly string[] SensitiveDirectories =
    {
        "essential",
        "PCL",
        "config/essential",
        "config/customskinloader"
    };

    /// <summary>账号 Token 相关的文件名关键字。</summary>
    private static readonly string[] TokenKeywords =
    {
        "token",
        "account",
        "credential",
        "session"
    };

    /// <summary>账号 Token 相关的文件扩展名。</summary>
    private static readonly string[] TokenExtensions =
    {
        ".token",
        ".credential",
        ".account"
    };

    /// <summary>可安全压缩的配置目录（JSON 去空白）。</summary>
    private static readonly string[] CompressibleDirectories =
    {
        "config",
        "defaultconfigs"
    };

    /// <summary>压缩收益低于此比例时放弃压缩（避免无意义改写）。</summary>
    private const double MinCompressionGainRatio = 0.05;

    public ExportOptimizer(ILogService log, IMessageService messageService)
    {
        _log = log;
        _messageService = messageService;
    }

    /// <summary>
    /// 执行完整的导出优化流水线。
    /// </summary>
    public async Task<OptimizationReport> OptimizeAsync(
        string serverDir,
        string modpackName,
        OptimizationConfig config,
        TargetPlatform platform = TargetPlatform.Both,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new OptimizationReport
        {
            DryRun = config.DryRun,
            ServerDirectory = serverDir
        };

        if (!Directory.Exists(serverDir))
        {
            report.Success = false;
            report.Warnings.Add($"服务端目录不存在，已跳过优化: {serverDir}");
            _log.Warn($"优化器跳过：目录不存在 {serverDir}");
            return report;
        }

        var memoryMb = AikarFlagsBuilder.ResolveMemoryMb(config.MemoryMb);
        report.MemoryMb = memoryMb;
        report.HighMemoryProfile = AikarFlagsBuilder.IsHighMemory(memoryMb);

        _log.Info($"开始导出优化: dryRun={config.DryRun}, memory={memoryMb}MB, highMemory={report.HighMemoryProfile}, platform={platform}");

        var backupRoot = config.Backup && !config.DryRun
            ? Path.Combine(serverDir, "backup", $"deearthx-{DateTime.Now:yyyyMMdd-HHmmss}")
            : null;
        report.BackupDirectory = backupRoot;

        if (backupRoot is not null)
        {
            try
            {
                Directory.CreateDirectory(backupRoot);
                Log(report, "backup", "create", "backup", $"备份目录已创建: {backupRoot}");
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"创建备份目录失败，已自动关闭备份: {ex.Message}");
                _log.Error("创建备份目录失败", ex);
                backupRoot = null;
                report.BackupDirectory = null;
            }
        }

        var ctx = new OptimizationContext(serverDir, backupRoot, config, report);

        if (config.StripClientResources)
        {
            await RunStepAsync(report, "剥离客户端资源", ct, () => StripClientResourcesAsync(ctx, ct), config);
        }

        if (config.SanitizeSensitive)
        {
            await RunStepAsync(report, "清理敏感信息", ct, () => SanitizeSensitiveAsync(ctx, ct), config);
        }

        if (config.DetectRedundant)
        {
            await RunStepAsync(report, "检测冗余文件", ct, () => DetectRedundantAsync(ctx, ct), config);
        }

        if (config.GenerateServerProperties)
        {
            await RunStepAsync(report, "生成 server.properties", ct, () => GenerateServerPropertiesAsync(ctx, ct), config);
        }

        if (config.StripClientResources || config.DetectRedundant)
        {
            await RunStepAsync(report, "压缩配置文件", ct, () => CompressConfigsAsync(ctx, ct), config);
        }

        if (config.GenerateLaunchScript || config.AikarFlags)
        {
            await RunStepAsync(report, "生成启动脚本", ct, () => GenerateLaunchScriptsAsync(ctx, platform, ct), config);
        }

        BuildAdvice(report, config);
        report.ElapsedMs = sw.ElapsedMilliseconds;

        Log(report, "report", "summarize", string.Empty,
            $"本次共处理 {report.TotalAffectedFiles} 项；耗时 {report.ElapsedMs}ms；dryRun={config.DryRun}");

        _log.Info($"导出优化完成: 处理项={report.TotalAffectedFiles}, 耗时={report.ElapsedMs}ms, 警告={report.Warnings.Count}");

        try
        {
            await _messageService.Info(
                $"[优化报告] 内存 {memoryMb}MB，处理 {report.TotalAffectedFiles} 项，生成脚本 {report.GeneratedScripts.Count} 个，耗时 {report.ElapsedMs}ms");
        }
        catch (Exception ex)
        {
            _log.Warn($"推送优化报告消息失败: {ex.Message}");
        }

        return report;
    }

    private async Task RunStepAsync(
        OptimizationReport report,
        string stepName,
        CancellationToken ct,
        Func<Task> step,
        OptimizationConfig config)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.Warnings.Add($"{stepName} 失败: {ex.Message}");
            _log.Error($"优化步骤失败: {stepName}", ex);
            Log(report, stepName, "error", string.Empty, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // 步骤 1：剥离客户端资源
    // ------------------------------------------------------------------
    private async Task StripClientResourcesAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var serverDir = ctx.ServerDir;

        foreach (var dirName in ClientResourceDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(serverDir, dirName);
            if (!Directory.Exists(full))
            {
                continue;
            }

            // logs/ 总会重新生成；其余目录整体移走。
            var moved = await MoveDirectoryAsync(ctx, full, Path.Combine("backup", "removed", dirName), "移除客户端资源目录");
            ctx.Report.RemovedClientResources.AddRange(moved);
            Log(ctx.Report, "strip-client-resources", ctx.Config.DryRun ? "detect" : "move", dirName,
                $"客户端专用目录，已{(ctx.Config.DryRun ? "计划" : "")}移入 backup/removed/（{moved.Count} 个文件）");
        }

        // 客户端专用配置文件（顶层）。
        foreach (var fileName in SensitiveFiles)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(serverDir, fileName);
            if (!File.Exists(full))
            {
                continue;
            }

            // options.txt / servers.dat 属于敏感信息，统一在敏感信息步骤处理；
            // 这里只处理其余客户端配置。
            if (fileName is "options.txt" or "servers.dat" or "servers.dat_old")
            {
                continue;
            }

            await MoveFileAsync(ctx, full, Path.Combine("backup", "removed", fileName), "移除客户端配置文件");
            ctx.Report.RemovedClientResources.Add(fileName);
            Log(ctx.Report, "strip-client-resources", ctx.Config.DryRun ? "detect" : "move", fileName,
                "客户端专用配置文件，服务端无需加载");
        }
    }

    // ------------------------------------------------------------------
    // 步骤 2：清理敏感信息
    // ------------------------------------------------------------------
    private async Task SanitizeSensitiveAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var serverDir = ctx.ServerDir;

        // 2.1 顶层已知敏感文件
        foreach (var fileName in SensitiveFiles)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(serverDir, fileName);
            if (!File.Exists(full))
            {
                continue;
            }

            await MoveFileAsync(ctx, full, Path.Combine("backup", "sensitive", fileName), "清理敏感信息");
            ctx.Report.SanitizedSensitiveFiles.Add(fileName);
            Log(ctx.Report, "sanitize-sensitive", ctx.Config.DryRun ? "detect" : "move", fileName,
                "可能包含玩家个人信息或账号凭据，导出时移除");
        }

        // 2.2 敏感目录下的文件（例如 Essential Mod 会保存登录 Token）
        foreach (var dir in SensitiveDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var fullDir = Path.Combine(serverDir, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(fullDir))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(serverDir, file);
                var lower = relative.ToLowerInvariant();

                var isSensitive = TokenExtensions.Any(ext => lower.EndsWith(ext))
                                  || TokenKeywords.Any(k => lower.Contains(k))
                                  || Path.GetFileName(lower) is "options.txt" or "servers.dat";

                if (!isSensitive)
                {
                    continue;
                }

                await MoveFileAsync(ctx, file, Path.Combine("backup", "sensitive", relative), "清理敏感信息");
                ctx.Report.SanitizedSensitiveFiles.Add(relative);
                Log(ctx.Report, "sanitize-sensitive", ctx.Config.DryRun ? "detect" : "move", relative,
                    "检测到账号 Token / 凭据类文件，导出时必须移除");
            }
        }

        // 2.3 全局扫描明显的 Token 文件（限制深度，避免拖慢导出）
        foreach (var file in SafeEnumerateFiles(serverDir, maxDepth: 4))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(serverDir, file);
            var lower = relative.ToLowerInvariant();

            // 跳过即将整体移除的目录与备份目录本身。
            if (lower.StartsWith("backup" + Path.DirectorySeparatorChar) || lower.StartsWith("backup/"))
            {
                continue;
            }

            if (ClientResourceDirectories.Any(d => lower.StartsWith(d.ToLowerInvariant() + Path.DirectorySeparatorChar)))
            {
                continue;
            }

            var fileName = Path.GetFileName(lower);
            var looksLikeToken = TokenExtensions.Any(ext => fileName.EndsWith(ext))
                                 || (fileName.Contains("token") && (fileName.EndsWith(".json") || fileName.EndsWith(".txt") || fileName.EndsWith(".dat")));

            if (!looksLikeToken)
            {
                continue;
            }

            if (ctx.Report.SanitizedSensitiveFiles.Any(p => string.Equals(p, relative, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            await MoveFileAsync(ctx, file, Path.Combine("backup", "sensitive", relative), "清理敏感信息");
            ctx.Report.SanitizedSensitiveFiles.Add(relative);
            Log(ctx.Report, "sanitize-sensitive", ctx.Config.DryRun ? "detect" : "move", relative,
                "文件名包含 token 关键字，疑似账号凭据");
        }
    }

    // ------------------------------------------------------------------
    // 步骤 3：冗余文件检测
    // ------------------------------------------------------------------
    private async Task DetectRedundantAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var serverDir = ctx.ServerDir;
        var modsDir = Path.Combine(serverDir, "mods");

        // 3.1 mods/ 中的 .disabled 残留
        if (Directory.Exists(modsDir))
        {
            foreach (var file in SafeEnumerateFiles(modsDir, maxDepth: 1))
            {
                ct.ThrowIfCancellationRequested();
                if (!file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(serverDir, file);
                var size = SafeFileSize(file);
                ctx.Report.Redundancies.Add(new RedundancyFinding
                {
                    Kind = "disabled-file",
                    Path = relative,
                    Description = "被手动禁用（.disabled）的旧模组文件，不会被加载",
                    SizeBytes = size,
                    Resolved = false
                });
                Log(ctx.Report, "detect-redundant", "detect", relative, "检测到 .disabled 残留文件");
            }
        }

        // 3.2 mods/ 与 libs/ 中同时存在的同名依赖
        var libsDir = Path.Combine(serverDir, "libs");
        if (Directory.Exists(modsDir) && Directory.Exists(libsDir))
        {
            var modNames = SafeEnumerateFiles(modsDir, maxDepth: 1)
                .Select(Path.GetFileName)
                .Where(n => n is not null && n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Select(n => n!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var lib in SafeEnumerateFiles(libsDir, maxDepth: 1))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(lib);
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !modNames.Contains(name))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(serverDir, lib);
                ctx.Report.Redundancies.Add(new RedundancyFinding
                {
                    Kind = "duplicate-library",
                    Path = relative,
                    Description = $"与 mods/{name} 重复的库文件，可能导致类冲突",
                    SizeBytes = SafeFileSize(lib),
                    Resolved = false
                });
                Log(ctx.Report, "detect-redundant", "detect", relative, $"与 mods/{name} 重复");
            }
        }

        // 3.3 mods/ 内部完全重复的同名 jar（大小写差异造成的重复）
        if (Directory.Exists(modsDir))
        {
            var byName = SafeEnumerateFiles(modsDir, maxDepth: 1)
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => Path.GetFileName(f).ToLowerInvariant())
                .Where(g => g.Count() > 1);

            foreach (var group in byName)
            {
                ct.ThrowIfCancellationRequested();
                // 保留第一个，其余标记为重复。
                foreach (var dup in group.Skip(1))
                {
                    var relative = Path.GetRelativePath(serverDir, dup);
                    ctx.Report.Redundancies.Add(new RedundancyFinding
                    {
                        Kind = "duplicate-mod",
                        Path = relative,
                        Description = $"与 {Path.GetFileName(group.First())} 同名的重复模组文件",
                        SizeBytes = SafeFileSize(dup),
                        Resolved = false
                    });
                    Log(ctx.Report, "detect-redundant", "detect", relative, "同名重复模组");
                }
            }
        }

        var totalBytes = ctx.Report.Redundancies.Sum(r => r.SizeBytes);
        if (ctx.Report.Redundancies.Count > 0)
        {
            ctx.Report.Suggestions.Add(
                $"检测到 {ctx.Report.Redundancies.Count} 项冗余文件（约 {FormatBytes(totalBytes)}），建议手动确认后清理；" +
                "冗余文件不会自动删除，以免破坏依赖关系。");
        }

        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // 步骤 4：server.properties
    // ------------------------------------------------------------------
    private async Task GenerateServerPropertiesAsync(OptimizationContext ctx, CancellationToken ct)
    {
        var path = Path.Combine(ctx.ServerDir, "server.properties");
        var existed = File.Exists(path);

        var changes = await ServerPropertiesFactory.EnsureAsync(ctx.ServerDir, ctx.Config.DryRun, ct);
        ctx.Report.ConfigChanges.AddRange(changes);

        foreach (var change in changes)
        {
            Log(ctx.Report, "generate-server-properties", ctx.Config.DryRun ? "detect" : "create",
                change.Key, $"{change.Before ?? "(未设置)"} → {change.After}（{change.Reason}）");
        }

        if (!existed)
        {
            ctx.Report.Suggestions.Add(
                "已生成 server.properties（原整合包未提供）。请务必确认 online-mode、level-name 等与你的部署方式一致。");
        }
    }

    // ------------------------------------------------------------------
    // 步骤 5：配置 JSON 压缩
    // ------------------------------------------------------------------
    private async Task CompressConfigsAsync(OptimizationContext ctx, CancellationToken ct)
    {
        long savedBytes = 0;
        var compressed = 0;

        foreach (var dirName in CompressibleDirectories)
        {
            var root = Path.Combine(ctx.ServerDir, dirName);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var original = await File.ReadAllTextAsync(file, ct);
                    if (string.IsNullOrWhiteSpace(original))
                    {
                        continue;
                    }

                    // 带注释的 JSON（JSON5/HOCON）不能安全压缩，跳过。
                    if (LooksLikeCommentedJson(original))
                    {
                        continue;
                    }

                    var parsed = JsonDocument.Parse(original);
                    var minified = JsonSerializer.Serialize(parsed, MinifyOptions);

                    var before = Encoding.UTF8.GetByteCount(original);
                    var after = Encoding.UTF8.GetByteCount(minified);
                    if (before <= 0 || after <= 0)
                    {
                        continue;
                    }

                    var gain = 1.0 - (double)after / before;
                    if (gain < MinCompressionGainRatio)
                    {
                        continue;
                    }

                    if (!ctx.Config.DryRun)
                    {
                        await BackupFileAsync(ctx, file);
                        await File.WriteAllTextAsync(file, minified, new UTF8Encoding(false), ct);
                    }

                    savedBytes += before - after;
                    compressed++;
                    Log(ctx.Report, "compress-configs", ctx.Config.DryRun ? "detect" : "compress",
                        Path.GetRelativePath(ctx.ServerDir, file),
                        $"{before} 字节 → {after} 字节（节省 {gain:P0}），去除多余空白以加快解析");
                }
                catch (JsonException)
                {
                    // 不是严格 JSON，跳过。
                }
                catch (Exception ex)
                {
                    ctx.Report.Warnings.Add($"压缩配置失败: {Path.GetFileName(file)} - {ex.Message}");
                }
            }
        }

        if (compressed > 0)
        {
            ctx.Report.Suggestions.Add(
                $"已压缩 {compressed} 个 JSON 配置文件，共节省 {FormatBytes(savedBytes)}，可略微缩短服务端启动解析时间。");
        }

        await Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions MinifyOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 粗略判断 JSON 中是否含注释（正则足够：字符串里的 // 会误判，但宁可跳过也不破坏文件）。
    /// </summary>
    private static bool LooksLikeCommentedJson(string text)
    {
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // 步骤 6：启动脚本
    // ------------------------------------------------------------------
    private async Task GenerateLaunchScriptsAsync(OptimizationContext ctx, TargetPlatform platform, CancellationToken ct)
    {
        var includeAikar = ctx.Config.AikarFlags;
        var memoryMb = ctx.Report.MemoryMb;

        var scripts = LaunchScriptGenerator.WriteScripts(
            ctx.ServerDir, platform, memoryMb, includeAikar, ctx.Config.DryRun);

        ctx.Report.GeneratedScripts.AddRange(scripts);
        ctx.Report.JvmArguments = AikarFlagsBuilder.BuildArguments(memoryMb, includeAikar);
        ctx.Report.FlagAdjustments = AikarFlagsBuilder.DescribeAdjustments(memoryMb);

        foreach (var script in scripts)
        {
            Log(ctx.Report, "generate-launch-script", ctx.Config.DryRun ? "detect" : "create", script,
                $"包含 {memoryMb}MB 堆内存{(includeAikar ? " 与 Aikar's Flags" : "")}");
        }

        if (ctx.Report.HighMemoryProfile)
        {
            ctx.Report.ExpectedGains.Add(
                $"内存 {memoryMb}MB 超过 12GB，已自动切换高内存参数组（G1NewSizePercent=40 / G1MaxNewSizePercent=50 / " +
                "G1HeapRegionSize=16M / G1ReservePercent=15 / InitiatingHeapOccupancyPercent=20）。");
        }

        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // 建议与收益说明
    // ------------------------------------------------------------------
    private static void BuildAdvice(OptimizationReport report, OptimizationConfig config)
    {
        if (report.DryRun)
        {
            report.Suggestions.Insert(0, "当前为 dry-run 模式：以下结果仅为预览，未对任何文件做出修改。");
        }

        if (report.BackupDirectory is not null)
        {
            report.Suggestions.Add($"被移除的内容已备份至 {report.BackupDirectory}，确认无误后可自行删除。");
        }
        else if (!config.Backup && !config.DryRun)
        {
            report.Suggestions.Add("备份已关闭：本次移除的文件无法从 backup/ 恢复，请自行确认。");
        }

        if (report.RemovedClientResources.Count > 0)
        {
            report.ExpectedGains.Add(
                $"移除 {report.RemovedClientResources.Count} 个客户端专用文件/目录，减小服务端体积并避免误加载客户端资源。");
        }

        if (report.SanitizedSensitiveFiles.Count > 0)
        {
            report.ExpectedGains.Add(
                $"清理 {report.SanitizedSensitiveFiles.Count} 个敏感信息文件（options.txt / servers.dat / 账号 Token 等），避免个人凭据随整合包泄露。");
        }

        if (report.GeneratedScripts.Count > 0)
        {
            report.ExpectedGains.Add(
                "启动脚本使用 G1GC + Aikar's Flags，可显著降低 GC 停顿；-Xms 与 -Xmx 相等可避免运行期堆扩容。");
        }

        if (report.ConfigChanges.Count > 0)
        {
            report.ExpectedGains.Add(
                $"server.properties 应用了 {report.ConfigChanges.Count} 项优化（视距、模拟距离、区块写入等），可降低单核 CPU 与 I/O 压力。");
        }

        if (report.Redundancies.Count > 0)
        {
            var bytes = report.Redundancies.Sum(r => r.SizeBytes);
            report.Suggestions.Add($"冗余文件合计约 {FormatBytes(bytes)}，清理后可缩短服务端扫描模组的时间。");
        }

        if (report.Suggestions.Count == 0)
        {
            report.Suggestions.Add("未发现需要手动处理的项目。");
        }
    }

    // ------------------------------------------------------------------
    // 文件操作辅助
    // ------------------------------------------------------------------

    /// <summary>
    /// 把整个目录移动到目标位置（目标相对于服务端目录）。返回被移动的文件相对路径列表。
    /// dry-run 时只统计不移动。
    /// </summary>
    private async Task<List<string>> MoveDirectoryAsync(
        OptimizationContext ctx,
        string sourceDir,
        string destinationRelative,
        string reason)
    {
        var moved = new List<string>();

        foreach (var file in SafeEnumerateFiles(sourceDir))
        {
            moved.Add(Path.GetRelativePath(ctx.ServerDir, file));
        }

        if (ctx.Config.DryRun)
        {
            return moved;
        }

        var destination = Path.Combine(ctx.ServerDir, destinationRelative);
        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            // 目标已存在时追加序号，避免互相覆盖。
            var finalDestination = destination;
            var suffix = 1;
            while (Directory.Exists(finalDestination))
            {
                finalDestination = $"{destination}-{suffix++}";
            }

            Directory.Move(sourceDir, finalDestination);
        }
        catch (Exception ex)
        {
            ctx.Report.Warnings.Add($"移动目录失败: {Path.GetFileName(sourceDir)} - {ex.Message}");
            _log.Warn($"移动目录失败 {sourceDir}: {ex.Message}");
        }

        await Task.CompletedTask;
        return moved;
    }

    /// <summary>
    /// 把单个文件移入目标位置，并在此之前备份。
    /// </summary>
    private async Task MoveFileAsync(
        OptimizationContext ctx,
        string sourceFile,
        string destinationRelative,
        string reason)
    {
        if (ctx.Config.DryRun)
        {
            return;
        }

        try
        {
            await BackupFileAsync(ctx, sourceFile);

            var destination = Path.Combine(ctx.ServerDir, destinationRelative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var finalDestination = destination;
            var suffix = 1;
            while (File.Exists(finalDestination))
            {
                finalDestination = $"{destination}-{suffix++}";
            }

            File.Move(sourceFile, finalDestination, overwrite: false);
        }
        catch (Exception ex)
        {
            ctx.Report.Warnings.Add($"移动文件失败: {Path.GetFileName(sourceFile)} - {ex.Message}");
            _log.Warn($"移动文件失败 {sourceFile}: {ex.Message}");
        }
    }

    /// <summary>
    /// 在修改文件之前把它复制到备份目录（保持相对路径结构）。
    /// </summary>
    private async Task BackupFileAsync(OptimizationContext ctx, string filePath)
    {
        if (ctx.BackupRoot is null)
        {
            return;
        }

        try
        {
            var relative = Path.GetRelativePath(ctx.ServerDir, filePath);
            var target = Path.Combine(ctx.BackupRoot, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!File.Exists(target))
            {
                File.Copy(filePath, target, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            ctx.Report.Warnings.Add($"备份失败: {Path.GetFileName(filePath)} - {ex.Message}");
            _log.Warn($"备份文件失败 {filePath}: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, int maxDepth = int.MaxValue)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    private static long SafeFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = (int)Math.Floor(Math.Log(bytes, 1024));
        if (index >= units.Length) index = units.Length - 1;
        return $"{bytes / Math.Pow(1024, index):0.##} {units[index]}";
    }

    private static void Log(OptimizationReport report, string step, string action, string path, string? detail)
    {
        report.Logs.Add(new OptimizationLogEntry
        {
            Step = step,
            Action = action,
            Path = path,
            Detail = detail
        });
    }

    /// <summary>单次优化执行的上下文。</summary>
    private sealed class OptimizationContext
    {
        public OptimizationContext(string serverDir, string? backupRoot, OptimizationConfig config, OptimizationReport report)
        {
            ServerDir = serverDir;
            BackupRoot = backupRoot;
            Config = config;
            Report = report;
        }

        public string ServerDir { get; }
        public string? BackupRoot { get; }
        public OptimizationConfig Config { get; }
        public OptimizationReport Report { get; }
    }
}
