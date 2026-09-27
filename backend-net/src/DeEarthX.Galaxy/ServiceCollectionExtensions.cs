using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Toml;
using DeEarthX.Infrastructure.Zip;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Galaxy;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXGalaxy(this IServiceCollection services)
    {
        services.AddSingleton<GalaxyService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var section = config.GetSection("Galaxy");
            return new GalaxyService(
                sp.GetRequiredService<IZipService>(),
                sp.GetRequiredService<ITomlService>(),
                sp.GetRequiredService<IDeEarthXHttpService>(),
                sp.GetRequiredService<ILogService>(),
                section["ApiBase"],
                section["ApiKey"]
            );
        });
        return services;
    }
}
