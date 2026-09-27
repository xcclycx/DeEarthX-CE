using DeEarthX.Dex;
using DeEarthX.Dex.Logging;
using DeEarthX.Dex.Optimization;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Dex;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeEarthXDex(this IServiceCollection services)
    {
        services.AddSingleton<DexService>();

        // 导出优化流水线（Aikar's Flags / 启动脚本 / 资源剥离 / 优化报告）。
        services.AddSingleton<ExportOptimizer>();

        // 日志查看与导出。
        services.AddSingleton<LogFileService>();

        return services;
    }
}
