namespace DeEarthX.Plugins;

/// <summary>
/// 宿主内进程插件（首方扩展、内置能力）的生命周期契约。
/// 进程式插件（manifest + main 可执行文件）不需要实现它，由 <see cref="PluginManager"/> 统一调度进程。
/// </summary>
public interface IPluginLifecycle
{
    /// <summary>稳定标识，与 manifest 的 id 对应。</summary>
    string Id { get; }

    /// <summary>加载完成后调用一次，用于读取配置、准备资源。</summary>
    Task InitializeAsync(PluginContext context, CancellationToken ct = default);

    /// <summary>插件被启用或首个钩子执行前调用。</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>插件被禁用、重载或宿主关闭时调用。必须可重复调用。</summary>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// 进程式插件的可选生命周期钩子：在 <c>manifest.json</c> 中设置
/// <c>"lifecycle": { "start": "lifecycle/start.js", "stop": "lifecycle/stop.js" }</c> 即可，
/// 宿主会以子进程方式调用，stdin 收到 JSON（事件名、插件 id、插件目录），stdout 忽略。
/// </summary>
public sealed class PluginLifecycleScripts
{
    public string? Start { get; set; }
    public string? Stop { get; set; }
}
