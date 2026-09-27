using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Process;

namespace DeEarthX.ModLoader;

public sealed class NeoForge : IXModLoader
{
    private const string MavenBase = "https://maven.neoforged.net/releases";

    private readonly string _mcv;
    private readonly string _mlv;
    private readonly string _path;
    private readonly IDownloadService _downloadService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly IProcessService _processService;
    private readonly ILogService _log;
    private readonly IConfigService _configService;

    public NeoForge(
        string mcv,
        string mlv,
        string path,
        IDownloadService downloadService,
        IDeEarthXHttpService httpService,
        IProcessService processService,
        ILogService log,
        IConfigService configService)
    {
        _mcv = mcv;
        _mlv = mlv;
        _path = path;
        _downloadService = downloadService;
        _httpService = httpService;
        _processService = processService;
        _log = log;
        _configService = configService;
    }

    public async Task SetupAsync(CancellationToken ct = default)
    {
        await InstallerAsync(ct).ConfigureAwait(false);

        var jarName = GetInstallerJarName();
        var installCmd = $"java -jar {jarName} --installServer";
        _log.Info($"NeoForge 执行安装: {installCmd}");
        await _processService.RunAsync(installCmd, _path, ct).ConfigureAwait(false);
    }

    public async Task InstallerAsync(CancellationToken ct = default)
    {
        var rawUrl = $"{MavenBase}/net/neoforged/neoforge/{_mlv}/neoforge-{_mlv}-installer.jar";
        var mirrorUrl = MirrorResolver.ResolveMavenUrl(rawUrl, _configService.Get());
        var filePath = Path.Combine(_path, GetInstallerJarName());

        if (mirrorUrl != rawUrl)
        {
            try
            {
                _log.Info($"下载 NeoForge installer (镜像): {mirrorUrl}");
                await _downloadService.DownloadFileAsync(mirrorUrl, filePath, ct: ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _log.Warn($"镜像下载失败，回退到官方源: {ex.Message}");
            }
        }

        _log.Info($"下载 NeoForge installer: {rawUrl}");
        await _downloadService.DownloadFileAsync(rawUrl, filePath, ct: ct).ConfigureAwait(false);
    }

    private string GetInstallerJarName() => $"neoforge-{_mcv}-{_mlv}-installer.jar";
}
