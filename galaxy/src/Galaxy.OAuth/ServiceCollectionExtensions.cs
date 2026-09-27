using Galaxy.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Galaxy.OAuth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyOAuth(this IServiceCollection services)
    {
        services.AddScoped<OAuth2Service>();
        services.AddScoped<DeveloperService>();
        services.AddScoped<OAuthAppService>();
        return services;
    }
}
