using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Models;
using DeEarthX.Infrastructure.Zip;
using DeEarthX.Realtime;

namespace DeEarthX.Platform;

public sealed class PlatformService
{
    private const string CurseforgeManifest = "manifest.json";
    private const string ModrinthManifest = "modrinth.index.json";

    private readonly CurseforgePlatform _curseforge;
    private readonly ModrinthPlatform _modrinth;
    private readonly IZipService _zip;
    private readonly ILogService _log;

    public PlatformService(
        CurseforgePlatform curseforge,
        ModrinthPlatform modrinth,
        IZipService zip,
        ILogService log,
        IServiceProvider services)
    {
        _curseforge = curseforge;
        _modrinth = modrinth;
        _zip = zip;
        _log = log;
        PlatformFactory.UseServiceProvider(services);
    }

    public PlatformType Detect(byte[] zipBuffer)
    {
        var entries = _zip.ReadEntries(zipBuffer);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (MatchesRootEntry(entry.FullName, CurseforgeManifest))
            {
                return PlatformType.Curseforge;
            }

            if (MatchesRootEntry(entry.FullName, ModrinthManifest))
            {
                return PlatformType.Modrinth;
            }
        }

        return PlatformType.Unknown;
    }

    private static bool MatchesRootEntry(string fullName, string expected)
    {
        return fullName.Equals(expected, StringComparison.OrdinalIgnoreCase)
               || fullName.EndsWith("/" + expected, StringComparison.OrdinalIgnoreCase);
    }

    public JsonObject? ReadManifest(byte[] zipBuffer, PlatformType type)
    {
        var entryName = type switch
        {
            PlatformType.Curseforge => CurseforgeManifest,
            PlatformType.Modrinth => ModrinthManifest,
            _ => null
        };

        if (entryName is null)
        {
            return null;
        }

        var fullName = ResolveEntryName(zipBuffer, entryName);
        if (fullName is null)
        {
            _log.Warn($"压缩包中未找到 {entryName}");
            return null;
        }

        var bytes = _zip.ReadEntry(zipBuffer, fullName);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        return JsonNode.Parse(text) as JsonObject;
    }

    private string? ResolveEntryName(byte[] zipBuffer, string entryName)
    {
        if (_zip.ReadEntry(zipBuffer, entryName) is not null)
        {
            return entryName;
        }

        foreach (var entry in _zip.ReadEntries(zipBuffer))
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (MatchesRootEntry(entry.FullName, entryName))
            {
                return entry.FullName;
            }
        }

        return null;
    }

    public ModpackInfo GetInfo(JsonObject manifest, PlatformType type)
    {
        return Select(type).GetInfo(manifest);
    }

    public Task DownloadFilesAsync(JsonObject manifest, PlatformType type, string destPath, IMessageService? message, CancellationToken ct)
    {
        return Select(type).DownloadFilesAsync(manifest, destPath, message, ct);
    }

    private IXPlatform Select(PlatformType type)
    {
        return type switch
        {
            PlatformType.Curseforge => _curseforge,
            PlatformType.Modrinth => _modrinth,
            _ => throw new NotSupportedException($"不支持的平台类型: {type}")
        };
    }
}
