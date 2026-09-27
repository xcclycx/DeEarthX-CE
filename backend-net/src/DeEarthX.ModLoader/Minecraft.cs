using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Process;

namespace DeEarthX.ModLoader;

public sealed class Minecraft : IXModLoader
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private const string EulaContent =
        "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
        "#Spawn by DeEarthX(QQgroup:559349662) Tianpao:(https://space.bilibili.com/1728953419)\n" +
        "eula=true";

    private readonly string _ml;
    private readonly string _mcv;
    private readonly string _mlv;
    private readonly string _path;
    private readonly IDownloadService _downloadService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly IProcessService _processService;
    private readonly ILogService _log;

    public Minecraft(
        string ml,
        string mcv,
        string mlv,
        string path,
        IDownloadService downloadService,
        IDeEarthXHttpService httpService,
        IProcessService processService,
        ILogService log)
    {
        _ml = ml;
        _mcv = mcv;
        _mlv = mlv;
        _path = path;
        _downloadService = downloadService;
        _httpService = httpService;
        _processService = processService;
        _log = log;
    }

    public async Task SetupAsync(CancellationToken ct = default)
    {
        await WriteEulaAsync(ct).ConfigureAwait(false);
        await DownloadServerJarAsync(ct).ConfigureAwait(false);
    }

    public Task InstallerAsync(CancellationToken ct = default)
    {
        return DownloadServerJarAsync(ct);
    }

    private async Task DownloadServerJarAsync(CancellationToken ct)
    {
        var serverUrl = await ResolveServerUrlAsync(ct).ConfigureAwait(false);
        var filePath = Path.Combine(_path, "server.jar");
        _log.Info($"下载原版服务端 server.jar: {serverUrl}");
        await _downloadService.DownloadFileAsync(serverUrl.Url, filePath, serverUrl.Sha1, ct: ct).ConfigureAwait(false);
    }

    private async Task<(string Url, string? Sha1)> ResolveServerUrlAsync(CancellationToken ct)
    {
        var manifest = await _httpService.GetJsonAsync<VersionManifest>(VersionManifestUrl, ct).ConfigureAwait(false);
        var entry = manifest.Versions.FirstOrDefault(v => string.Equals(v.Id, _mcv, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"未在版本清单中找到 Minecraft 版本: {_mcv}");

        var version = await _httpService.GetJsonAsync<VersionJson>(entry.Url, ct).ConfigureAwait(false);
        var server = version.Downloads?.Server
                     ?? throw new InvalidOperationException($"版本 {_mcv} 未提供 server 下载信息");

        return (server.Url, server.Sha1);
    }

    private async Task WriteEulaAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_path);
        await File.WriteAllTextAsync(Path.Combine(_path, "eula.txt"), EulaContent, ct).ConfigureAwait(false);
    }

    private sealed class VersionManifest
    {
        public List<VersionEntry> Versions { get; set; } = new();
    }

    private sealed class VersionEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    private sealed class VersionJson
    {
        public VersionDownloads? Downloads { get; set; }
    }

    private sealed class VersionDownloads
    {
        public DownloadRef? Server { get; set; }
    }

    private sealed class DownloadRef
    {
        public string Url { get; set; } = string.Empty;
        public string? Sha1 { get; set; }
    }
}
