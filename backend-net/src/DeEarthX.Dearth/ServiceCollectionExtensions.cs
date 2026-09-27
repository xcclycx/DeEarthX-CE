using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Dearth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXDearth(this IServiceCollection services)
    {
        services.AddSingleton<FileExtractor>();
        services.AddSingleton<FileOperator>();
        services.AddSingleton<HashFilter>();
        services.AddSingleton<DexpubFilter>();
        services.AddSingleton<MixinFilter>();
        services.AddSingleton<ModrinthFilter>();
        services.AddSingleton<ModFilterService>();
        services.AddSingleton<ModCheckService>();
        return services;
    }
}
