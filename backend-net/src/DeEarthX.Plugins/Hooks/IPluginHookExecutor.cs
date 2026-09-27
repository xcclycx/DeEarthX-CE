namespace DeEarthX.Plugins;

public enum PluginHook
{
    BeforeModpackProcess,
    AfterModpackProcess,
    BeforeFilterMods,
    AfterFilterMods,
    BeforeDownloadFiles,
    AfterDownloadFiles,
    BeforeInstallLoader,
    AfterInstallLoader,

    /// <summary>输出压缩包前（旧版 <c>OnOutputZip</c> 的历史名称，两者等价）。</summary>
    OnOutputZip,

    /// <summary>单个模组过滤判定。插件通过 <c>extra.filter = true</c> 表示应过滤该模组。</summary>
    FilterStrategy,

    /// <summary>插件安装完成后。</summary>
    PluginInstalled,

    /// <summary>插件被启用/禁用后。</summary>
    PluginStateChanged,

    /// <summary>整合包处理失败时，允许插件做兜底或上报。</summary>
    OnError
}

/// <summary>钩子元数据：名称、默认超时、是否传递二进制数据。</summary>
public sealed record PluginHookDescriptor(PluginHook Hook, string Name, TimeSpan DefaultTimeout, bool DataAware);

/// <summary>钩子执行结果。</summary>
public sealed record HookResult(bool Success, byte[]? Data, object? Extra, string? Error)
{
    public static HookResult Ok(byte[]? data = null, object? extra = null)
        => new(true, data, extra, null);

    public static HookResult Fail(string error)
        => new(false, null, null, error);
}

/// <summary>钩子调用的输入上下文。</summary>
public sealed record HookContext(
    string Hook,
    byte[]? Data,
    Dictionary<string, object?> Meta)
{
    /// <summary>单插件执行超时；null 表示使用钩子默认值。</summary>
    public TimeSpan? Timeout { get; init; }

    public static readonly IReadOnlySet<PluginHook> DataAwareHooks = new HashSet<PluginHook>
    {
        PluginHook.BeforeModpackProcess,
        PluginHook.AfterModpackProcess,
        PluginHook.OnOutputZip
    };

    /// <summary>
    /// 把钩子名解析成宿主内置枚举：未知名称（插件自定义钩子）返回 null，
    /// 由调用方按自定义名称处理。
    /// </summary>
    public static PluginHook? ParseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.Equals("filter_strategy", StringComparison.OrdinalIgnoreCase))
        {
            return PluginHook.FilterStrategy;
        }

        // 旧名 beforeFilterMods 归一到过滤扩展点，避免同一扩展点出现两条路径。
        if (name.Equals("beforeFilterMods", StringComparison.OrdinalIgnoreCase))
        {
            return PluginHook.FilterStrategy;
        }

        return Enum.TryParse<PluginHook>(name, ignoreCase: true, out var hook) ? hook : null;
    }

    /// <summary>
    /// 把插件清单里声明的钩子名归类。过滤扩展点同时接受
    /// <c>filter_strategy</c>（推荐）与旧名 <c>beforeFilterMods</c>。
    /// </summary>
    public static string NameOf(PluginHook hook) => hook.ToString();

    public static PluginHookDescriptor Describe(PluginHook hook) => new(
        hook,
        hook.ToString(),
        DefaultTimeouts.TryGetValue(hook, out var timeout) ? timeout : DefaultTimeout,
        DataAwareHooks.Contains(hook));

    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<PluginHook, TimeSpan> DefaultTimeouts = new()
    {
        [PluginHook.BeforeModpackProcess] = TimeSpan.FromMinutes(5),
        [PluginHook.AfterModpackProcess] = TimeSpan.FromMinutes(5),
        [PluginHook.BeforeDownloadFiles] = TimeSpan.FromMinutes(2),
        [PluginHook.AfterDownloadFiles] = TimeSpan.FromMinutes(2),
        [PluginHook.OnOutputZip] = TimeSpan.FromMinutes(2),
        [PluginHook.FilterStrategy] = TimeSpan.FromSeconds(20),
        [PluginHook.PluginInstalled] = TimeSpan.FromSeconds(30),
        [PluginHook.PluginStateChanged] = TimeSpan.FromSeconds(30),
        [PluginHook.OnError] = TimeSpan.FromSeconds(60)
    };
}

public interface IPluginHookExecutor
{
    Task<HookResult> ExecuteAsync(PluginHook hook, HookContext context, CancellationToken ct);
}

/// <summary>
/// 钩子执行器。
/// <para>相较于旧实现的三项优化：</para>
/// <list type="number">
///   <item><b>执行计划缓存</b>：按 (插件版本, 钩子名) 缓存“哪些插件需要执行”，
///         模组过滤阶段原来每个模组都会重新遍历全部插件并重复解析清单，现在只做一次
///         字典查找；</item>
///   <item><b>超时隔离</b>：每个插件独立超时，卡死不再阻塞整条流水线；</item>
///   <item><b>数据回传与附加结果</b>：二进制走 <c>data</c>/<c>dataPath</c>，
///         结构化结果走 <c>extra</c>，过滤策略等扩展点因此可用。</item>
/// </list>
/// </para>
/// </summary>
public interface IPluginHookPipeline
{
    /// <summary>执行一个钩子，返回最终数据、附加结果与失败列表。</summary>
    Task<HookPipelineResult> RunAsync(PluginHook hook, HookContext context, CancellationToken ct = default);

    /// <summary>所有已声明的钩子名（含插件未启用的），用于诊断。</summary>
    IReadOnlyCollection<string> DeclaredHooks { get; }
}

public sealed record HookPipelineResult(
    byte[]? Data,
    object? Extra,
    IReadOnlyList<PluginHookInvocation> Invocations)
{
    public bool AnySucceeded => Invocations.Any(i => i.Success);

    public bool AnyFailed => Invocations.Any(i => !i.Success);

    public IReadOnlyList<PluginHookInvocation> Failures => Invocations.Where(i => !i.Success).ToList();
}

public sealed record PluginHookInvocation(
    string PluginId,
    bool Success,
    bool TimedOut,
    long ElapsedMilliseconds,
    string? Error);
