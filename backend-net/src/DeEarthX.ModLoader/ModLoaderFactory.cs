using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Process;

namespace DeEarthX.ModLoader;

public sealed class ModLoaderFactory
{
    private readonly IDownloadService _downloadService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly IProcessService _processService;
    private readonly ILogService _log;
    private readonly IConfigService _configService;

    public ModLoaderFactory(
        IDownloadService downloadService,
        IDeEarthXHttpService httpService,
        IProcessService processService,
        ILogService log,
        IConfigService configService)
    {
        _downloadService = downloadService;
        _httpService = httpService;
        _processService = processService;
        _log = log;
        _configService = configService;
    }

    public IXModLoader Create(string ml, string mcv, string mlv, string path)
    {
        switch (ml)
        {
            case "fabric":
            case "fabric-loader":
                return new Fabric(mcv, mlv, path, _downloadService, _httpService, _processService, _log);
            case "forge":
                return new Forge(mcv, mlv, path, _downloadService, _httpService, _processService, _log, _configService);
            case "neoforge":
                return new NeoForge(mcv, mlv, path, _downloadService, _httpService, _processService, _log, _configService);
            default:
                return new Minecraft(ml, mcv, mlv, path, _downloadService, _httpService, _processService, _log);
        }
    }

    public IXModLoader CreateMinecraft(string ml, string mcv, string mlv, string path)
    {
        return new Minecraft(ml, mcv, mlv, path, _downloadService, _httpService, _processService, _log);
    }
}
