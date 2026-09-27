using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Platform;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXPlatform(this IServiceCollection services)
    {
        services.AddSingleton<CurseforgePlatform>();
        services.AddSingleton<ModrinthPlatform>();
        services.AddSingleton<PlatformService>();
        return services;
    }
}
