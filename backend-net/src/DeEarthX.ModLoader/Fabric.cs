using System.Text.Json.Serialization;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Process;

namespace DeEarthX.ModLoader;

public sealed class Fabric : IXModLoader
{
    private const string MetaBase = "https://meta.fabricmc.net/v2";

    private readonly string _mcv;
    private readonly string _mlv;
    private readonly string _path;
    private readonly IDownloadService _downloadService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly IProcessService _processService;
    private readonly ILogService _log;

    public Fabric(
        string mcv,
        string mlv,
        string path,
        IDownloadService downloadService,
        IDeEarthXHttpService httpService,
        IProcessService processService,
        ILogService log)
    {
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
        await InstallerAsync(ct).ConfigureAwait(false);

        var installCmd = $"java -jar fabric-installer.jar server -dir . -mcversion {_mcv} -loader {_mlv}";
        _log.Info($"Fabric 执行安装: {installCmd}");
        await _processService.RunAsync(installCmd, _path, ct).ConfigureAwait(false);

        await WriteLaunchScriptsAsync(ct).ConfigureAwait(false);
    }

    public async Task InstallerAsync(CancellationToken ct = default)
    {
        var entries = await _httpService.GetJsonAsync<List<FabricInstallerEntry>>($"{MetaBase}/versions/installer", ct).ConfigureAwait(false);

        var stable = entries.FirstOrDefault(e => e.Stable) ?? entries.FirstOrDefault();
        if (stable is null || string.IsNullOrEmpty(stable.Url))
        {
            throw new InvalidOperationException("未找到可用的 Fabric installer 版本");
        }

        var filePath = Path.Combine(_path, "fabric-installer.jar");
        _log.Info($"下载 Fabric installer: {stable.Url}");
        await _downloadService.DownloadFileAsync(stable.Url, filePath, ct: ct).ConfigureAwait(false);
    }

    private async Task WriteLaunchScriptsAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_path);

        var bat = "@echo off" + Environment.NewLine + "java -jar fabric-server-launch.jar" + Environment.NewLine;
        var sh = "#!/bin/bash\njava -jar fabric-server-launch.jar\n";

        await File.WriteAllTextAsync(Path.Combine(_path, "run.bat"), bat, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_path, "run.sh"), sh, ct).ConfigureAwait(false);
    }

    private sealed class FabricInstallerEntry
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }
}
