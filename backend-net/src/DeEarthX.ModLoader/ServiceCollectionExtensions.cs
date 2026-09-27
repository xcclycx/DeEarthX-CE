using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.ModLoader;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXModLoader(this IServiceCollection services)
    {
        services.AddSingleton<ModLoaderFactory>();
        services.AddSingleton<IModLoaderService, ModLoaderService>();

        return services;
    }
}
