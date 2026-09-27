using System;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Platform;

public static class PlatformFactory
{
    private static IServiceProvider? _services;
    private static readonly object SyncRoot = new();

    internal static void UseServiceProvider(IServiceProvider services)
    {
        lock (SyncRoot)
        {
            _services = services;
        }
    }

    public static IXPlatform Create(PlatformType type)
    {
        IServiceProvider? services;
        lock (SyncRoot)
        {
            services = _services;
        }

        if (services is null)
        {
            throw new InvalidOperationException(
                "PlatformFactory 尚未配置 ServiceProvider，请先调用 AddDeEarthXPlatform 并解析 PlatformService。");
        }

        return type switch
        {
            PlatformType.Curseforge => services.GetRequiredService<CurseforgePlatform>(),
            PlatformType.Modrinth => services.GetRequiredService<ModrinthPlatform>(),
            _ => throw new NotSupportedException($"不支持的平台类型: {type}")
        };
    }
}
