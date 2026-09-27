using Galaxy.Core;
using Galaxy.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Galaxy.Auth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyAuth(this IServiceCollection services)
    {
        services.AddSingleton<AuthMiddleware>();
        services.AddScoped<AuthService>();
        services.AddScoped<EmailService>();
        return services;
    }
}
