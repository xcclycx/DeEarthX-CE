namespace DeEarthX.Infrastructure.Diagnostics;

/// <summary>一条程序错误记录。</summary>
public sealed class ErrorRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Source { get; set; }

    /// <summary>是否已经被前端读取过；用于“有新错误”角标。</summary>
    public bool Acknowledged { get; set; }
}

/// <summary>
/// 进程内的错误环形缓冲。让前端能够在不解析日志文件的情况下拿到“最近发生的程序错误”。
///
/// 静态实现是有意的：<see cref="FileLogger"/> 由 DI 以单例注册，但错误提示需要在
/// 任何阶段（包括容器尚未完全就绪时）都能记录，因此不依赖服务解析。
/// </summary>
public static class ErrorRingBuffer
{
    private const int Capacity = 100;

    private static readonly object Lock = new();
    private static readonly Queue<ErrorRecord> Buffer = new(Capacity);

    /// <summary>记录一条错误。</summary>
    public static void Push(string message, string? detail = null, string? source = null, string level = "error")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var record = new ErrorRecord
        {
            Level = level,
            Message = message,
            Detail = detail,
            Source = source
        };

        lock (Lock)
        {
            while (Buffer.Count >= Capacity)
            {
                Buffer.Dequeue();
            }

            Buffer.Enqueue(record);
        }
    }

    /// <summary>记录一条警告。</summary>
    public static void PushWarning(string message, string? detail = null, string? source = null)
        => Push(message, detail, source, "warn");

    /// <summary>读取最近的错误记录（最新在前）。</summary>
    public static List<ErrorRecord> Snapshot(int limit = 50, bool onlyUnacknowledged = false)
    {
        lock (Lock)
        {
            var query = Buffer.AsEnumerable();
            if (onlyUnacknowledged)
            {
                query = query.Where(r => !r.Acknowledged);
            }

            return query.Reverse().Take(Math.Clamp(limit, 1, Capacity)).ToList();
        }
    }

    /// <summary>未确认的错误数量，用于前端角标。</summary>
    public static int UnacknowledgedCount()
    {
        lock (Lock)
        {
            return Buffer.Count(r => !r.Acknowledged);
        }
    }

    /// <summary>把记录标记为已读。</summary>
    public static int Acknowledge(IEnumerable<string>? ids = null)
    {
        lock (Lock)
        {
            var idSet = ids?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var count = 0;

            foreach (var record in Buffer)
            {
                if (record.Acknowledged)
                {
                    continue;
                }

                if (idSet is null || idSet.Contains(record.Id))
                {
                    record.Acknowledged = true;
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>清空缓冲区。</summary>
    public static void Clear()
    {
        lock (Lock)
        {
            Buffer.Clear();
        }
    }
}
