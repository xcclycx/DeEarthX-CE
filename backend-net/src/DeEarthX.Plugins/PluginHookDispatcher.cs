using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

/// <summary>
/// 默认钩子执行器：把钩子分发给声明了它的、且处于启用状态的全部插件。
/// </summary>
public sealed class PluginHookDispatcher : IPluginHookExecutor, IPluginHookPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = DeEarthXJsonOptions.Default;

    private readonly PluginManager _pluginManager;
    private readonly IPluginProcessRunner _runner;
    private readonly IPluginEventBus _events;
    private readonly ILogService _log;

    private readonly ConcurrentDictionary<string, HookPlan> _plans = new(StringComparer.Ordinal);
    private long _planVersion = -1;
    private readonly object _planGate = new();

    public PluginHookDispatcher(
        PluginManager pluginManager,
        IPluginProcessRunner runner,
        IPluginEventBus events,
        ILogService log)
    {
        _pluginManager = pluginManager;
        _runner = runner;
        _events = events;
        _log = log;
    }

    /// <summary>兼容旧接口：只返回“是否成功 + 最终数据 + 最后一个插件的附加结果”。</summary>
    public async Task<HookResult> ExecuteAsync(PluginHook hook, HookContext context, CancellationToken ct)
    {
        var result = await RunAsync(hook, context, ct).ConfigureAwait(false);
        var lastExtra = result.Invocations.Count > 0 ? result.Extra : null;
        return result.AnyFailed && !result.AnySucceeded
            ? HookResult.Fail(string.Join("; ", result.Failures.Select(f => $"{f.PluginId}: {f.Error}")))
            : new HookResult(true, result.Data, lastExtra, null);
    }

    public async Task<HookPipelineResult> RunAsync(PluginHook hook, HookContext context, CancellationToken ct = default)
    {
        var descriptor = HookContext.Describe(hook);
        var plan = await GetPlanAsync(descriptor, ct).ConfigureAwait(false);
        var hookName = CanonicalName(hook);

        var invocations = new List<PluginHookInvocation>(plan.Steps.Count);
        if (plan.Steps.Count == 0)
        {
            return new HookPipelineResult(context.Data, null, invocations);
        }

        var currentData = context.Data;
        object? lastExtra = null;
        var timeout = context.Timeout ?? descriptor.DefaultTimeout;

        foreach (var step in plan.Steps)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                var payload = BuildPayload(descriptor, hookName, step, context, currentData);
                var json = JsonSerializer.Serialize(payload, JsonOptions);

                var result = await _runner
                    .RunAsync(step.EntryPath, step.Plugin.DirectoryPath, json, timeout, ct)
                    .ConfigureAwait(false);

                var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                if (!result.Success)
                {
                    _log.Warn($"插件 {step.Plugin.Manifest.Id} 执行钩子 {hookName} 失败: {result.Error}");
                    invocations.Add(new PluginHookInvocation(step.Plugin.Manifest.Id, false, result.TimedOut, elapsed, result.Error));
                    continue;
                }

                if (descriptor.DataAware && result.Data is { Length: > 0 })
                {
                    currentData = result.Data;
                }

                if (result.Extra is not null)
                {
                    lastExtra = result.Extra;
                }

                invocations.Add(new PluginHookInvocation(step.Plugin.Manifest.Id, true, false, elapsed, null));

                // 让插件之间可以通过事件总线互相通知，而不必都去抢同一个钩子。
                await _events.PublishAsync(
                    hookName,
                    new PluginEventArgs
                    {
                        Payload = payload,
                        Meta = context.Meta,
                        Result = { ["pluginId"] = step.Plugin.Manifest.Id }
                    },
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _log.Error($"执行插件 {step.Plugin.Manifest.Id} 的钩子 {hookName} 失败", ex);
                invocations.Add(new PluginHookInvocation(step.Plugin.Manifest.Id, false, false, elapsed, ex.Message));
            }
        }

        return new HookPipelineResult(currentData, lastExtra, invocations);
    }

    public IReadOnlyCollection<string> DeclaredHooks
    {
        get
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in _pluginManager.SnapshotPlugins())
            {
                foreach (var hook in plugin.Manifest.Capabilities.Hooks)
                {
                    names.Add(hook);
                }
            }

            return names;
        }
    }

    /// <summary>插件可见的稳定钩子名：过滤策略统一叫 <c>filter_strategy</c>，其余用枚举名。</summary>
    private static string CanonicalName(PluginHook hook)
        => hook == PluginHook.FilterStrategy ? "filter_strategy" : hook.ToString();

    private static object BuildPayload(
        PluginHookDescriptor descriptor,
        string hookName,
        HookStep step,
        HookContext context,
        byte[]? currentData)
    {
        var meta = context.Meta;

        return new
        {
            hook = hookName,
            pluginId = step.Plugin.Manifest.Id,
            pluginDirectory = step.Plugin.DirectoryPath,
            dataDirectory = step.DataDirectory,
            modpackName = GetMeta(meta, "modpackName"),
            minecraft = GetMeta(meta, "minecraft"),
            loader = GetMeta(meta, "loader"),
            filePath = GetMeta(meta, "filePath"),
            minecraftVersion = GetMeta(meta, "minecraftVersion"),
            strategyName = GetMeta(meta, "strategyName"),
            strategyType = GetMeta(meta, "strategyType"),
            modId = GetMeta(meta, "modId"),
            fileName = GetMeta(meta, "fileName"),
            settings = step.Plugin.Config.Settings,
            data = descriptor.DataAware && currentData is not null ? Convert.ToBase64String(currentData) : null
        };
    }

    private static string? GetMeta(Dictionary<string, object?> meta, string key)
        => meta.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>
    /// 取得执行计划。计划按插件集合版本缓存：只要插件没有被增删改，同一个钩子
    /// 无论被调用多少次（例如逐模组调用数千次）都不再重新遍历插件。
    /// </summary>
    private async ValueTask<HookPlan> GetPlanAsync(PluginHookDescriptor descriptor, CancellationToken ct)
    {
        var version = _pluginManager.Version;
        if (_planVersion == version && _plans.TryGetValue(descriptor.Name, out var cached))
        {
            return cached;
        }

        var plugins = await _pluginManager.GetPluginsAsync(ct).ConfigureAwait(false);

        lock (_planGate)
        {
            if (_planVersion != version)
            {
                _plans.Clear();
                _planVersion = version;
            }

            if (_plans.TryGetValue(descriptor.Name, out var existing))
            {
                return existing;
            }

            var steps = new List<HookStep>();
            foreach (var plugin in plugins)
            {
                // 未启用的插件不参与；声明了钩子的插件即便 autoStart=false 也保持旧行为（照常执行）。
                if (!plugin.Config.Enabled)
                {
                    continue;
                }

                if (!MatchesHook(plugin, descriptor.Name))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(plugin.Manifest.Capabilities.HookEntry))
                {
                    _log.Warn($"插件 {plugin.Manifest.Id} 声明了钩子 {descriptor.Name} 但未设置 main/entryPoint，已跳过");
                    continue;
                }

                var entryPath = PluginPaths.ResolveInside(plugin.DirectoryPath, plugin.Manifest.Capabilities.HookEntry!);
                if (!File.Exists(entryPath))
                {
                    _log.Warn($"插件 {plugin.Manifest.Id} 的入口文件不存在: {entryPath}");
                    continue;
                }

                steps.Add(new HookStep(plugin, entryPath, plugin.DirectoryPath));
            }

            var plan = new HookPlan(steps);
            _plans[descriptor.Name] = plan;
            return plan;
        }
    }

    private sealed record HookPlan(IReadOnlyList<HookStep> Steps);

    /// <summary>
    /// 清单里的钩子名与宿主钩子名的匹配规则：
    /// <list type="bullet">
    ///   <item>完全相等（不区分大小写），或 <c>beforeFilterMods</c> / <c>filter_strategy</c> 等价；</item>
    ///   <item>以下划线/点号分隔的限定名，如 <c>filter_strategy.mymod</c> 匹配策略 <c>mymod</c>。</item>
    /// </list>
    /// </summary>
    private static bool MatchesHook(LoadedPlugin plugin, string hookName)
    {
        // filter_strategy 是过滤扩展点的首选名称，但旧插件可能声明 beforeFilterMods，两者等价。
        var isFilterHook = string.Equals(hookName, nameof(PluginHook.FilterStrategy), StringComparison.OrdinalIgnoreCase);

        foreach (var declared in plugin.Manifest.Capabilities.Hooks)
        {
            if (string.Equals(declared, hookName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (isFilterHook && string.Equals(declared, nameof(PluginHook.BeforeFilterMods), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record HookStep(LoadedPlugin Plugin, string EntryPath, string DataDirectory);
}
