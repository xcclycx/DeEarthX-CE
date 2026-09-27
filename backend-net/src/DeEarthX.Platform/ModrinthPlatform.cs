using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Models;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Realtime;

namespace DeEarthX.Platform;

public sealed class ModrinthPlatform : IXPlatform
{
    private static readonly string[] LoaderKeys = { "fabric-loader", "forge", "neoforge" };

    private readonly IDownloadService _downloadService;
    private readonly IConfigService _configService;
    private readonly ILogService _log;

    public ModrinthPlatform(
        IDownloadService downloadService,
        IConfigService configService,
        ILogService log)
    {
        _downloadService = downloadService;
        _configService = configService;
        _log = log;
    }

    public ModpackInfo GetInfo(JsonObject manifest)
    {
        var deps = manifest["dependencies"] as JsonObject;
        var minecraft = deps?["minecraft"].AsString() ?? string.Empty;

        var loader = string.Empty;
        var version = string.Empty;
        if (deps is not null)
        {
            foreach (var kv in deps)
            {
                var key = kv.Key;
                if (key == "minecraft")
                {
                    continue;
                }

                if (IsLoaderKey(key))
                {
                    loader = key;
                    version = kv.Value?.AsString() ?? string.Empty;
                }
            }
        }

        return new ModpackInfo(minecraft, loader, version);
    }

    private static bool IsLoaderKey(string key)
    {
        foreach (var loader in LoaderKeys)
        {
            if (loader == key)
            {
                return true;
            }
        }

        return false;
    }

    public async Task DownloadFilesAsync(JsonObject manifest, string destPath, IMessageService? message, CancellationToken ct = default)
    {
        if (manifest["files"] is not JsonArray files || files.Count == 0)
        {
            return;
        }

        var config = _configService.Get();
        var items = new List<DownloadItem>();

        foreach (var entry in files)
        {
            var relPath = entry?["path"].AsString();
            if (string.IsNullOrEmpty(relPath))
            {
                continue;
            }

            if (relPath.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry?["downloads"] is not JsonArray downloads || downloads.Count == 0)
            {
                continue;
            }

            var firstUrl = downloads[0].AsString();
            if (string.IsNullOrEmpty(firstUrl))
            {
                continue;
            }

            var realUrl = MirrorResolver.ResolveModrinthCdnUrl(firstUrl, config);
            var sha1 = (entry["hashes"] as JsonObject)?["sha1"].AsString();

            var filePath = Path.Combine(destPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            items.Add(new DownloadItem(realUrl, filePath, sha1));
        }

        if (items.Count == 0)
        {
            return;
        }

        _log.Info($"Modrinth 准备下载 {items.Count} 个文件到 {destPath}");
        var progress = CurseforgePlatform.BuildProgress(message);
        await _downloadService.WFastDownloadAsync(items, progress, true, true, ct);
    }
}
