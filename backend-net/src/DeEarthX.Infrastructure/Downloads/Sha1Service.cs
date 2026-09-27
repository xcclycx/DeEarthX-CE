using System.Security.Cryptography;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Infrastructure.Downloads;

public sealed class Sha1Service
{
    private const int BufferSize = 64 * 1024;
    private const int MaxCacheSize = 500;
    private static readonly Dictionary<string, HashCacheEntry> Cache = new();
    private static readonly object CacheLock = new();
    private readonly ILogService _log;

    public Sha1Service(ILogService log)
    {
        _log = log;
    }

    public string Calculate(string filePath)
    {
        var cacheKey = Path.GetFullPath(filePath);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var entry) && IsCacheValid(filePath, entry))
            {
                _log.Debug($"使用缓存的哈希值: {filePath}");
                return entry.Hash;
            }
        }

        var hash = ComputeFileSha1(filePath);

        try
        {
            var stats = new FileInfo(filePath);
            lock (CacheLock)
            {
                if (Cache.Count >= MaxCacheSize)
                {
                    var firstKey = Cache.Keys.First();
                    Cache.Remove(firstKey);
                }
                Cache[cacheKey] = new HashCacheEntry(hash, stats.LastWriteTimeUtc, stats.Length);
            }
        }
        catch
        {
        }

        return hash;
    }

    public bool Verify(string filePath, string expectedHash)
    {
        var actual = Calculate(filePath);
        var expected = expectedHash.ToLowerInvariant();
        var match = actual == expected;

        if (!match)
        {
            _log.Error($"文件哈希验证失败: {filePath}");
            _log.Error($"期望: {expected}");
            _log.Error($"实际: {actual}");
        }
        else
        {
            _log.Debug($"文件哈希验证成功: {filePath} (sha1: {actual})");
        }

        return match;
    }

    private static bool IsCacheValid(string filePath, HashCacheEntry entry)
    {
        try
        {
            var stats = new FileInfo(filePath);
            return stats.LastWriteTimeUtc == entry.Mtime && stats.Length == entry.Size;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeFileSha1(string filePath)
    {
        using var sha = SHA1.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        var bytes = sha.ComputeHash(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record HashCacheEntry(string Hash, DateTime Mtime, long Size);
}
