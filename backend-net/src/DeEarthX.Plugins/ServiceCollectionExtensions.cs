using DeEarthX.Core.Filter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeEarthX.Plugins;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册插件系统。所有内部协作者都注册为单例并面向接口，
    /// 替换其中任意一环（例如换掉进程执行器）不需要改动 <see cref="PluginManager"/>。
    /// </summary>
    public static IServiceCollection AddDeEarthXPlugins(this IServiceCollection services)
    {
        // 核心状态
        services.TryAddSingleton<PluginExtensionCatalog>();
        services.TryAddSingleton<IPluginEventBus, PluginEventBus>();
        services.TryAddSingleton<IPluginConfigStore, PluginConfigStore>();
        services.TryAddSingleton<PluginContextFactory>();
        services.TryAddSingleton<IPluginLoader, PluginLoader>();
        services.TryAddSingleton<PluginValidator>();
        services.TryAddSingleton<IPluginTemplateGenerator, PluginTemplateGenerator>();
        services.TryAddSingleton<IPluginProcessRunner, PluginProcessRunner>();

        // 门面：PluginManager 被钩子执行器反向依赖，因此必须是单例。
        services.TryAddSingleton<PluginManager>();

        // 钩子执行：同一个实例同时暴露新旧两个接口。
        services.TryAddSingleton<PluginHookDispatcher>();
        services.TryAddSingleton<IPluginHookExecutor>(sp => sp.GetRequiredService<PluginHookDispatcher>());
        services.TryAddSingleton<IPluginHookPipeline>(sp => sp.GetRequiredService<PluginHookDispatcher>());

        // 过滤策略聚合
        services.TryAddSingleton<PluginFilterStrategyProvider>();

        // 热重载
        services.AddHostedService<PluginHotReloadWatcher>();

        return services;
    }

    /// <summary>
    /// 注册一个宿主内置的进程内插件。它和用户插件出现在同一个列表里，
    /// 支持启用/禁用与生命周期回调，能力通过 <see cref="PluginContext.Extensions"/> 提供。
    /// </summary>
    public static IServiceCollection AddDeEarthXInProcessPlugin<T>(this IServiceCollection services)
        where T : class, IPluginLifecycle
    {
        services.AddSingleton<T>();
        services.AddSingleton<IPluginLifecycle>(sp => sp.GetRequiredService<T>());
        return services;
    }

    /// <summary>
    /// 注册一个扩展点贡献（例如一条内置过滤策略）。
    /// 贡献对象由 <paramref name="factory"/> 创建，可以是任意类型，无需事先注册到容器。
    /// </summary>
    public static IServiceCollection AddDeEarthXPluginExtension<T>(
        this IServiceCollection services,
        string kind,
        string key,
        Func<IServiceProvider, T> factory,
        int priority = 0)
        where T : class
    {
        services.AddSingleton(sp =>
        {
            var catalog = sp.GetRequiredService<PluginExtensionCatalog>();
            var contribution = factory(sp);

            // 宿主内置贡献的生命周期跟随容器，owner 用固定前缀与用户插件区分。
            catalog.Register(kind, key, contribution, "host", priority);
            return new ExtensionHolder<T>(contribution);
        });

        return services;
    }

    /// <summary>便捷方法：注册一条内置 <see cref="IFilterStrategy"/>。</summary>
    public static IServiceCollection AddDeEarthXFilterStrategy<T>(this IServiceCollection services, string key, int priority = 0)
        where T : class, IFilterStrategy
    {
        services.AddSingleton<T>();
        return services.AddDeEarthXPluginExtension<IFilterStrategy>(
            PluginExtensionKinds.FilterStrategy,
            key,
            sp => sp.GetRequiredService<T>(),
            priority);
    }

    /// <summary>
    /// 占位类型：让扩展注册随容器一起实例化，同时不污染其他服务的解析。
    /// </summary>
    private sealed record ExtensionHolder<T>(T Value) where T : class;
}
