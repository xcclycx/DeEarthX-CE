using System.Collections.Concurrent;
using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Infrastructure;

public sealed class FileLogger : ILogService, IDisposable
{
    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly object _consoleLock = new();
    private readonly object _bufferLock = new();
    private readonly List<string> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly string _logsDir;

    private const int BufferSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 日志一律以 UTF-8（无 BOM）写入。此前使用 <c>File.AppendAllText</c> 的默认编码，
    /// 在非 UTF-8 默认代码页下会把中文写成乱码，「日志」页面和导出文件都会受影响。
    /// </summary>
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private string _currentDate = TodayString();
    private string _logFilePath;
    private int _currentFileSize;

    public FileLogger(IAppDirectoryProvider appDirectoryProvider)
    {
        _appDirectoryProvider = appDirectoryProvider;
        _logsDir = Path.Combine(appDirectoryProvider.GetAppDirectory(), "logs");
        try
        {
            Directory.CreateDirectory(_logsDir);
        }
        catch
        {
        }

        _logFilePath = Path.Combine(_logsDir, $"deearthx-{_currentDate}.log");

        try
        {
            _currentFileSize = File.Exists(_logFilePath)
                ? (int)Math.Min(int.MaxValue, new FileInfo(_logFilePath).Length)
                : 0;
        }
        catch
        {
            _currentFileSize = 0;
        }

        _flushTimer = new Timer(_ => FlushSafe(), null, FlushInterval, FlushInterval);
    }

    public void Debug(string message, object? meta = null) => Log("debug", message, meta);
    public void Info(string message, object? meta = null) => Log("info", message, meta);
    public void Warn(string message, object? meta = null) => Log("warn", message, meta);
    public void Error(string message, object? meta = null) => Log("error", message, meta);

    private void Log(string level, string message, object? meta)
    {
        var timestamp = FormatTime();
        var metaStr = MetaToString(meta);

        WriteToFile(level, timestamp, message, metaStr);
        WriteToConsole(level, timestamp, message, metaStr);

        // 错误与警告同时写入内存环形缓冲，供前端“程序错误提醒”即时读取，
        // 避免为了弹一条提示去解析整个日志文件。
        if (level == "error")
        {
            Diagnostics.ErrorRingBuffer.Push(message, meta is Exception ex ? ex.ToString() : metaStr?.Trim(), "core", "error");
        }
        else if (level == "warn")
        {
            Diagnostics.ErrorRingBuffer.PushWarning(message, metaStr?.Trim(), "core");
        }
    }

    private void WriteToFile(string level, string timestamp, string message, string metaStr)
    {
        var line = $"{timestamp} [{level.ToUpperInvariant()}] {message}{metaStr}{Environment.NewLine}";
        lock (_bufferLock)
        {
            _buffer.Add(line);
            if (_buffer.Count >= BufferSize)
            {
                FlushLocked();
            }
        }
    }

    private void WriteToConsole(string level, string timestamp, string message, string metaStr)
    {
        var (levelColor, msgColor) = LevelColors(level);
        lock (_consoleLock)
        {
            var prevForeground = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(timestamp);
                Console.Write(' ');

                Console.ForegroundColor = levelColor;
                Console.Write($"[{level.ToUpperInvariant()}]");

                Console.ForegroundColor = msgColor;
                Console.Write(' ');
                Console.Write(message);

                if (!string.IsNullOrEmpty(metaStr))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(metaStr);
                }

                Console.WriteLine();
            }
            finally
            {
                Console.ForegroundColor = prevForeground;
            }
        }
    }

    private static (ConsoleColor level, ConsoleColor msg) LevelColors(string level) => level switch
    {
        "debug" => (ConsoleColor.DarkGray, ConsoleColor.DarkGray),
        "info" => (ConsoleColor.Green, ConsoleColor.White),
        "warn" => (ConsoleColor.Yellow, ConsoleColor.Yellow),
        "error" => (ConsoleColor.Red, ConsoleColor.Red),
        _ => (ConsoleColor.White, ConsoleColor.White)
    };

    private static string FormatTime()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string MetaToString(object? meta)
    {
        if (meta is null)
        {
            return string.Empty;
        }

        if (meta is Exception ex)
        {
            return $" {ex.Message} {ex.StackTrace}";
        }

        try
        {
            return " " + JsonSerializer.Serialize(meta, DeEarthXJsonOptions.Default);
        }
        catch
        {
            return " " + meta.ToString();
        }
    }

    private static string TodayString() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private void UpdateLogFilePath()
    {
        var today = TodayString();
        if (today != _currentDate)
        {
            _currentDate = today;
            _logFilePath = Path.Combine(_logsDir, $"deearthx-{_currentDate}.log");
        }
    }

    private void FlushSafe()
    {
        try
        {
            lock (_bufferLock)
            {
                FlushLocked();
            }
        }
        catch
        {
        }
    }

    private void FlushLocked()
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        UpdateLogFilePath();
        var snapshot = _buffer.ToArray();
        _buffer.Clear();
        var payload = string.Concat(snapshot);

        try
        {
            // 按配置的体积上限轮转，避免单个日志文件无限增长拖慢查看与导出。
            var maxBytes = GetMaxFileSizeBytes();
            if (maxBytes > 0 && _currentFileSize > 0 && _currentFileSize + payload.Length > maxBytes)
            {
                RotateLogFile();
            }

            File.AppendAllText(_logFilePath, payload, Utf8NoBom);
            _currentFileSize += payload.Length;
        }
        catch
        {
            lock (_bufferLock)
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    _buffer.Insert(i, snapshot[i]);
                }
            }
        }
    }

    /// <summary>
    /// 把当前日志改名成带序号的归档文件，并开始写新的一份。
    /// </summary>
    private void RotateLogFile()
    {
        try
        {
            var baseName = Path.Combine(_logsDir, $"deearthx-{_currentDate}");
            var index = 1;

            while (File.Exists($"{baseName}.{index}.log"))
            {
                index++;
            }

            File.Move(_logFilePath, $"{baseName}.{index}.log");
        }
        catch
        {
            // 轮转失败时继续追加写入，不影响日志功能。
        }

        _currentFileSize = 0;
    }

    /// <summary>
    /// 从配置文件直接读取日志体积上限（MB）。
    /// 这里刻意不注入 <see cref="IConfigService"/>：日志服务是配置服务的依赖，
    /// 注入会造成循环依赖。
    /// </summary>
    private int GetMaxFileSizeBytes()
    {
        try
        {
            var path = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "config.json");
            if (!File.Exists(path))
            {
                return 10 * 1024 * 1024;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("log", out var logNode)
                && logNode.ValueKind == JsonValueKind.Object
                && logNode.TryGetProperty("maxFileSizeMb", out var sizeNode)
                && sizeNode.TryGetInt32(out var mb)
                && mb > 0)
            {
                return mb * 1024 * 1024;
            }
        }
        catch
        {
            // 配置读取失败时使用默认值。
        }

        return 10 * 1024 * 1024;
    }

    public void Dispose()
    {
        try
        {
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _flushTimer.Dispose();
        }
        catch
        {
        }
        FlushSafe();
    }
}
