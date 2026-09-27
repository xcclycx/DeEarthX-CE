using System.IO.Compression;

namespace DeEarthX.Infrastructure.Zip;

public sealed record ZipEntryInfo(string FullName, long Length, bool IsDirectory);

public interface IZipService
{
    List<ZipEntryInfo> ReadEntries(byte[] buffer);
    List<ZipEntryInfo> ReadEntries(string filePath);
    byte[]? ReadEntry(byte[] buffer, string entryName);
    Task<Dictionary<string, byte[]>> ReadEntriesAsync(byte[] buffer, IEnumerable<string> names);

    Task CreateZipAsync(string sourceDir, string outputPath, int level = 9, CancellationToken ct = default);
    Task ExtractToDirectoryAsync(string zipPath, string destDir, CancellationToken ct = default);
    Task ExtractToDirectoryAsync(byte[] buffer, string destDir, CancellationToken ct = default);
    Task<byte[]> ReadEntryAsync(string zipPath, string entryName, CancellationToken ct = default);
}

public sealed class ZipService : IZipService
{
    public List<ZipEntryInfo> ReadEntries(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        return ReadEntriesCore(archive);
    }

    public List<ZipEntryInfo> ReadEntries(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        return ReadEntriesCore(archive);
    }

    private static List<ZipEntryInfo> ReadEntriesCore(ZipArchive archive)
    {
        var list = new List<ZipEntryInfo>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            var isDir = entry.FullName.EndsWith('/') || entry.Length == 0 && string.IsNullOrEmpty(entry.Name);
            list.Add(new ZipEntryInfo(entry.FullName, entry.Length, isDir));
        }
        return list;
    }

    public byte[]? ReadEntry(byte[] buffer, string entryName)
    {
        using var ms = new MemoryStream(buffer);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var entry = FindEntry(archive, entryName);
        return entry is null ? null : ReadEntryBytes(entry);
    }

    public Task<byte[]> ReadEntryAsync(string zipPath, string entryName, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = FindEntry(archive, entryName);
            return entry is null ? Array.Empty<byte>() : ReadEntryBytes(entry);
        }, ct);
    }

    public async Task<Dictionary<string, byte[]>> ReadEntriesAsync(byte[] buffer, IEnumerable<string> names)
    {
        var wanted = names.ToHashSet();
        var result = new Dictionary<string, byte[]>(wanted.Count);

        using var ms = new MemoryStream(buffer);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (wanted.Count == 0)
            {
                break;
            }

            if (!wanted.Remove(entry.FullName))
            {
                continue;
            }

            result[entry.FullName] = await ReadEntryBytesAsync(entry).ConfigureAwait(false);
        }

        return result;
    }

    public async Task CreateZipAsync(string sourceDir, string outputPath, int level = 9, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var compressionLevel = level switch
        {
            0 => CompressionLevel.NoCompression,
            <= 5 => CompressionLevel.Fastest,
            _ => CompressionLevel.Optimal
        };

        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        var baseDirFull = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(file);
            var relative = full.Substring(baseDirFull.Length).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, compressionLevel);
            entry.LastWriteTime = new FileInfo(file).LastWriteTime;
            await using var entryStream = entry.Open();
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await input.CopyToAsync(entryStream, 81920, ct).ConfigureAwait(false);
        }
    }

    public async Task ExtractToDirectoryAsync(string zipPath, string destDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        await ExtractCoreAsync(archive, destDir, ct).ConfigureAwait(false);
    }

    public async Task ExtractToDirectoryAsync(byte[] buffer, string destDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        using var ms = new MemoryStream(buffer);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        await ExtractCoreAsync(archive, destDir, ct).ConfigureAwait(false);
    }

    private static async Task ExtractCoreAsync(ZipArchive archive, string destDir, CancellationToken ct)
    {
        var destRoot = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var fullName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destPath = Path.GetFullPath(Path.Combine(destRoot, fullName));
            if (!destPath.StartsWith(destRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (entry.Length == 0 && (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name)))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            await using var entryStream = entry.Open();
            await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await entryStream.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string entryName)
    {
        return archive.GetEntry(entryName)
               ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var ms = new MemoryStream(checked((int)entry.Length));
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
