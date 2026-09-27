using System.Text;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Configuration;

namespace DeEarthX.Dex.Logging;

/// <summary>日志文件摘要信息。</summary>
public sealed class LogFileInfo
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public int LineCount { get; set; }
}

/// <summary>一行日志。</summary>
public sealed class LogLine
{
    public int LineNumber { get; set; }
    public string Level { get; set; } = "info";
    public string Timestamp { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

/// <summary>查询结果。</summary>
public sealed class LogQueryResult
{
    public List<LogLine> Lines { get; set; } = new();
    public int TotalMatched { get; set; }
    public int ScannedFiles { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>
/// 日志读取 / 导出 / 清理。日志由 <c>FileLogger</c> 写入 &lt;应用目录&gt;/logs/deearthx-yyyy-MM-dd.log。
/// </summary>
public sealed class LogFileService
{
    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly IConfigService _configService;
    private readonly ILogService _log;

    /// <summary>单次查询最多返回的行数，避免前端被超大日志拖垮。</summary>
    private const int MaxLinesPerQuery = 5000;

    public LogFileService(IAppDirectoryProvider appDirectoryProvider, IConfigService configService, ILogService log)
    {
        _appDirectoryProvider = appDirectoryProvider;
        _configService = configService;
        _log = log;
    }

    public string LogsDirectory => Path.Combine(_appDirectoryProvider.GetAppDirectory(), "logs");

    /// <summary>列出所有日志文件，最新在前。</summary>
    public List<LogFileInfo> ListFiles()
    {
        var dir = LogsDirectory;
        var result = new List<LogFileInfo>();

        if (!Directory.Exists(dir))
        {
            return result;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _log.Warn($"枚举日志目录失败: {ex.Message}");
            return result;
        }

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                result.Add(new LogFileInfo
                {
                    Name = info.Name,
                    SizeBytes = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    LineCount = 0
                });
            }
            catch
            {
                // 单个文件读取失败时跳过。
            }
        }

        return result.OrderByDescending(f => f.LastModified).ToList();
    }

    /// <summary>
    /// 读取日志。file 为空时读取最新一个（或全部）日志文件。
    /// </summary>
    public async Task<LogQueryResult> ReadAsync(
        string? file,
        string? level,
        string? keyword,
        int maxLines,
        bool allFiles,
        CancellationToken ct = default)
    {
        var result = new LogQueryResult();
        var dir = LogsDirectory;
        if (!Directory.Exists(dir))
        {
            return result;
        }

        var limit = maxLines <= 0 ? 500 : Math.Min(maxLines, MaxLinesPerQuery);

        List<string> targets;
        if (!string.IsNullOrWhiteSpace(file))
        {
            var resolved = ResolveLogPath(file);
            if (resolved is null)
            {
                return result;
            }
            targets = new List<string> { resolved };
        }
        else
        {
            var files = ListFiles();
            targets = allFiles
                ? files.Select(f => Path.Combine(dir, f.Name)).ToList()
                : files.Take(1).Select(f => Path.Combine(dir, f.Name)).ToList();
        }

        var levels = ParseLevels(level);
        var hasKeyword = !string.IsNullOrWhiteSpace(keyword);
        var keywordSpan = hasKeyword ? keyword!.Trim() : null;

        var matched = new List<LogLine>();
        var lineNumber = 0;

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            result.ScannedFiles++;

            try
            {
                // 共享读，避免和正在写入的 FileLogger 冲突。
                await using var stream = new FileStream(
                    target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var raw = await reader.ReadLineAsync(ct);
                    if (raw is null)
                    {
                        break;
                    }

                    lineNumber++;
                    var parsed = ParseLine(raw, Path.GetFileName(target), lineNumber);

                    if (levels is not null && !levels.Contains(parsed.Level))
                    {
                        continue;
                    }

                    if (keywordSpan is not null &&
                        !parsed.Message.Contains(keywordSpan, StringComparison.OrdinalIgnoreCase) &&
                        !parsed.Timestamp.Contains(keywordSpan, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (matched.Count < limit)
                    {
                        matched.Add(parsed);
                    }
                    else
                    {
                        result.Truncated = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"读取日志文件失败 {target}: {ex.Message}");
            }
        }

        // 最新的日志在最后，反转后给前端“最新在前”的体验。
        matched.Reverse();
        result.Lines = matched;
        result.TotalMatched = lineNumber;
        return result;
    }

    /// <summary>
    /// 导出一个或多个日志文件为单个字节数组（可按需只导出当前筛选结果）。
    /// </summary>
    public async Task<(byte[] Content, string FileName)> ExportAsync(
        string? file,
        string? level,
        string? keyword,
        bool allFiles,
        CancellationToken ct = default)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        if (allFiles || string.IsNullOrWhiteSpace(file))
        {
            var files = ListFiles();
            if (files.Count == 0)
            {
                var empty = Encoding.UTF8.GetBytes("# 暂无日志记录\n");
                return (empty, $"deearthx-logs-{stamp}.txt");
            }

            var sb = new StringBuilder();
            sb.Append("# DeEarthX 日志导出\n");
            sb.Append($"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            sb.Append($"# 日志目录: {LogsDirectory}\n");
            sb.Append($"# 文件数量: {files.Count}\n");
            sb.Append(new string('=', 60)).Append('\n').Append('\n');

            foreach (var info in files.OrderBy(f => f.LastModified))
            {
                ct.ThrowIfCancellationRequested();
                sb.Append("---- ").Append(info.Name).Append(" ----\n");

                try
                {
                    await using var stream = new FileStream(
                        Path.Combine(LogsDirectory, info.Name), FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var content = await reader.ReadToEndAsync(ct);
                    sb.Append(content);
                    if (!content.EndsWith('\n'))
                    {
                        sb.Append('\n');
                    }
                }
                catch (Exception ex)
                {
                    sb.Append($"# 读取失败: {ex.Message}\n");
                }

                sb.Append('\n');
            }

            return (Encoding.UTF8.GetBytes(sb.ToString()), $"deearthx-logs-{stamp}.txt");
        }

        // 导出当前筛选结果。
        var query = await ReadAsync(file, level, keyword, MaxLinesPerQuery, allFiles: false, ct);
        var header = new StringBuilder();
        header.Append("# DeEarthX 日志导出（已筛选）\n");
        header.Append($"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        header.Append($"# 来源文件: {file}\n");
        header.Append($"# 级别筛选: {(string.IsNullOrWhiteSpace(level) ? "全部" : level)}\n");
        header.Append($"# 关键字: {(string.IsNullOrWhiteSpace(keyword) ? "无" : keyword)}\n");
        header.Append($"# 行数: {query.Lines.Count}\n");
        header.Append(new string('=', 60)).Append('\n').Append('\n');

        foreach (var line in query.Lines.AsEnumerable().Reverse())
        {
            header.Append(line.Timestamp).Append(" [").Append(line.Level.ToUpperInvariant()).Append("] ")
                  .Append(line.Message).Append('\n');
        }

        var name = Path.GetFileNameWithoutExtension(file);
        return (Encoding.UTF8.GetBytes(header.ToString()), $"deearthx-{name}-{stamp}.txt");
    }

    /// <summary>
    /// 按留存策略清理过期日志。返回删除的文件数。
    /// </summary>
    public int Cleanup()
    {
        var config = _configService.Get().Log ?? LogConfig.CreateDefault();
        var retentionDays = config.RetentionDays <= 0 ? 14 : config.RetentionDays;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var removed = 0;

        foreach (var info in ListFiles())
        {
            try
            {
                if (info.LastModified >= cutoff)
                {
                    continue;
                }

                File.Delete(Path.Combine(LogsDirectory, info.Name));
                removed++;
                _log.Info($"已清理过期日志: {info.Name}");
            }
            catch (Exception ex)
            {
                _log.Warn($"清理日志失败 {info.Name}: {ex.Message}");
            }
        }

        return removed;
    }

    /// <summary>删除全部日志文件。返回删除数量。</summary>
    public int ClearAll()
    {
        var removed = 0;
        foreach (var info in ListFiles())
        {
            try
            {
                File.Delete(Path.Combine(LogsDirectory, info.Name));
                removed++;
            }
            catch (Exception ex)
            {
                _log.Warn($"删除日志失败 {info.Name}: {ex.Message}");
            }
        }

        if (removed > 0)
        {
            _log.Info($"已清空 {removed} 个日志文件");
        }

        return removed;
    }

    // ------------------------------------------------------------------

    private string? ResolveLogPath(string fileName)
    {
        // 只允许访问 logs 目录下的 .log 文件，防止路径穿越。
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || !safeName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var full = Path.Combine(LogsDirectory, safeName);
        var fullResolved = Path.GetFullPath(full);
        var rootResolved = Path.GetFullPath(LogsDirectory);

        if (!fullResolved.StartsWith(rootResolved, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(fullResolved) ? fullResolved : null;
    }

    private static HashSet<string>? ParseLevels(string? level)
    {
        if (string.IsNullOrWhiteSpace(level) || level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part.ToLowerInvariant());
        }

        return set.Count == 0 ? null : set;
    }

    /// <summary>
    /// 解析 <c>yyyy-MM-dd HH:mm:ss [LEVEL] message</c> 格式的一行。
    /// </summary>
    private static LogLine ParseLine(string raw, string file, int lineNumber)
    {
        var line = new LogLine
        {
            LineNumber = lineNumber,
            File = file,
            Level = "info",
            Message = raw
        };

        if (raw.Length < 22)
        {
            return line;
        }

        // 时间戳固定 19 字符。
        var bracketStart = raw.IndexOf('[', 19);
        if (bracketStart < 0)
        {
            return line;
        }

        var bracketEnd = raw.IndexOf(']', bracketStart);
        if (bracketEnd < 0)
        {
            return line;
        }

        line.Timestamp = raw[..bracketStart].Trim();
        line.Level = raw[(bracketStart + 1)..bracketEnd].Trim().ToLowerInvariant();
        line.Message = raw[(bracketEnd + 1)..].TrimStart();

        return line;
    }
}
