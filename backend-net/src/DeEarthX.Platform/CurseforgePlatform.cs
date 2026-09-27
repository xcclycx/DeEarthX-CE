using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Models;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Realtime;

namespace DeEarthX.Platform;

public sealed class CurseforgePlatform : IXPlatform
{
    private readonly IDownloadService _downloadService;
    private readonly IDeEarthXHttpService _http;
    private readonly IConfigService _configService;
    private readonly ILogService _log;

    public CurseforgePlatform(
        IDownloadService downloadService,
        IDeEarthXHttpService http,
        IConfigService configService,
        ILogService log)
    {
        _downloadService = downloadService;
        _http = http;
        _configService = configService;
        _log = log;
    }

    public ModpackInfo GetInfo(JsonObject manifest)
    {
        var minecraft = manifest["minecraft"]?["version"].AsString() ?? string.Empty;
        var loaderId = manifest["minecraft"]?["modLoaders"]?[0]?["id"].AsString() ?? string.Empty;
        var (loader, loaderVersion) = ParseLoaderId(loaderId);
        return new ModpackInfo(minecraft, loader, loaderVersion);
    }

    private static (string Loader, string Version) ParseLoaderId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return (string.Empty, string.Empty);
        }

        var idx = id.IndexOf('-');
        if (idx < 0)
        {
            return (id, string.Empty);
        }

        return (id.Substring(0, idx), id.Substring(idx + 1));
    }

    public async Task DownloadFilesAsync(JsonObject manifest, string destPath, IMessageService? message, CancellationToken ct = default)
    {
        if (manifest["files"] is not JsonArray filesNode || filesNode.Count == 0)
        {
            return;
        }

        var fileIds = new List<long>();
        foreach (var item in filesNode)
        {
            var fileId = item?["fileID"].AsLong();
            if (fileId.HasValue)
            {
                fileIds.Add(fileId.Value);
            }
        }

        if (fileIds.Count == 0)
        {
            return;
        }

        var mirror = MirrorResolver.Get(_configService.Get());
        var endpoint = $"{mirror.CurseforgeUrl.TrimEnd('/')}/v1/mods/files";

        _log.Info($"Curseforge 解析 {fileIds.Count} 个文件元数据: {endpoint}");
        var response = await _http.PostJsonAsync<JsonNode>(endpoint, new { fileIds }, ct);
        if (response["data"] is not JsonArray data || data.Count == 0)
        {
            _log.Warn("Curseforge 未返回任何文件元数据");
            return;
        }

        var modsDir = Path.Combine(destPath, "mods");
        Directory.CreateDirectory(modsDir);

        var config = _configService.Get();
        var items = new List<DownloadItem>();
        foreach (var entry in data)
        {
            var fileName = entry?["fileName"].AsString();
            var downloadUrl = entry?["downloadUrl"].AsString();
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(downloadUrl))
            {
                continue;
            }

            if (fileName.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var realUrl = MirrorResolver.ResolveCurseforgeCdnUrl(downloadUrl, config);
            var filePath = Path.Combine(modsDir, fileName);
            items.Add(new DownloadItem(realUrl, filePath));
        }

        if (items.Count == 0)
        {
            return;
        }

        var progress = BuildProgress(message);
        await _downloadService.WFastDownloadAsync(items, progress, false, true, ct);
    }

    internal static IProgress<DownloadProgress>? BuildProgress(IMessageService? message)
    {
        if (message is null)
        {
            return null;
        }

        return new Progress<DownloadProgress>(p =>
        {
            _ = message.Download(p.Total, p.Index, p.Name);
        });
    }
}
