using Microsoft.Extensions.DependencyInjection;

namespace Galaxy.Mods;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyMods(this IServiceCollection services)
    {
        services.AddScoped<ModService>();
        return services;
    }
}
