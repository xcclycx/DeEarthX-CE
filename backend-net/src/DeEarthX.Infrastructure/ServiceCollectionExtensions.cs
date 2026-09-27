using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Crypto;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.TextEncoding;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Java;
using DeEarthX.Infrastructure.Process;
using DeEarthX.Infrastructure.Toml;
using DeEarthX.Infrastructure.Zip;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXInfrastructure(this IServiceCollection services)
    {
        EncodingInitializer.Initialize();

        services.AddSingleton<IAppDirectoryProvider, AppDirectoryProvider>();
        services.AddSingleton<ILogService, FileLogger>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IDexpCrypto, DexpCrypto>();
        services.AddSingleton<Sha1Service>();
        services.AddSingleton<ITomlService, TomlService>();
        services.AddSingleton<IZipService, ZipService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IJavaService, JavaService>();

        services.AddHttpClient<IDeEarthXHttpService, DeEarthXHttpService>((_, client) => DeEarthXHttpService.Configure(client));
        services.AddHttpClient<IDownloadService, DownloadService>((_, client) => DeEarthXHttpService.Configure(client));

        return services;
    }
}
