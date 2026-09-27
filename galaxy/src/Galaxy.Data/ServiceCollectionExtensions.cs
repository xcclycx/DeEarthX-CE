using Galaxy.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Galaxy.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyData(this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<GalaxyDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddDbContextFactory<GalaxyDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddScoped<GalaxyDbInitializer>();
        return services;
    }
}
