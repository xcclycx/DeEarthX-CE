using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Zip;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Templates;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXTemplates(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<TemplateManager>();
        return services;
    }
}
