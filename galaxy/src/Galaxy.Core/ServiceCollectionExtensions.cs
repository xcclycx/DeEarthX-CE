using Microsoft.Extensions.DependencyInjection;

namespace Galaxy.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyCore(this IServiceCollection services)
    {
        services.Configure<GalaxyConfig>(opt => { });
        return services;
    }
}
