using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Filter;

namespace DeEarthX.Plugins;

/// <summary>
/// 把插件声明的过滤策略适配成宿主的 <see cref="IFilterStrategy"/>。
/// <para>旧实现有两个缺陷，这里都已修正：</para>
/// <list type="number">
///   <item>它固定调用 <c>BeforeFilterMods</c> 这个“整批过滤前”的钩子，
///         因此每个模组都会触发一次全量钩子流水线（上千次进程启动），
///         且过滤判定根本无法定位到具体模组 —— 现在改用 <c>filter_strategy</c> 扩展点；</item>
///   <item>它读取 <c>result.Extra</c> 而旧执行器永远返回 null，判定逻辑实际上从未生效 ——
///         现在执行器会把插件的 <c>extra</c> 结构化结果透传出来。</item>
/// </list>
/// </summary>
public sealed class PluginFilterStrategyAdapter : IFilterStrategy
{
    private readonly LoadedPlugin _plugin;
    private readonly PluginFilterStrategyDef _def;
    private readonly IPluginHookExecutor _hookExecutor;
    private readonly IPluginHookPipeline? _pipeline;

    public PluginFilterStrategyAdapter(
        LoadedPlugin plugin,
        PluginFilterStrategyDef def,
        IPluginHookExecutor hookExecutor)
    {
        _plugin = plugin;
        _def = def;
        _hookExecutor = hookExecutor;
        _pipeline = hookExecutor as IPluginHookPipeline;
    }

    public string Name => string.IsNullOrWhiteSpace(_def.Name) ? $"{_plugin.Manifest.Id}-filter" : _def.Name;

    public async Task<bool> ShouldFilterAsync(FilterContext context)
        => await ShouldFilterAsync(context, CancellationToken.None).ConfigureAwait(false);

    public async Task<bool> ShouldFilterAsync(FilterContext context, CancellationToken ct)
    {
        var meta = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fileName"] = context.FileName,
            ["filePath"] = context.FilePath,
            ["modId"] = context.ModId,
            ["minecraftVersion"] = context.MinecraftVersion,
            ["loader"] = context.Loader,
            ["strategyName"] = _def.Name,
            ["strategyType"] = _def.Type,
            ["pluginId"] = _plugin.Manifest.Id
        };

        if (context.Extra is not null)
        {
            foreach (var kv in context.Extra)
            {
                meta[kv.Key] = kv.Value;
            }
        }

        var hookContext = new HookContext("filter_strategy", null, meta);

        // 优先走新扩展点（可拿到 extra 与逐插件诊断）；拿不到时回退到旧契约。
        if (_pipeline is not null)
        {
            var pipelineResult = await _pipeline.RunAsync(PluginHook.FilterStrategy, hookContext, ct).ConfigureAwait(false);
            return Evaluate(pipelineResult.Extra);
        }

        var result = await _hookExecutor.ExecuteAsync(PluginHook.FilterStrategy, hookContext, ct).ConfigureAwait(false);
        return result.Success && Evaluate(result.Extra);
    }

    /// <summary>
    /// 判定插件返回值。支持三种写法，全部可选：
    /// <code>
    /// { "extra": { "filter": true } }
    /// { "extra": { "shouldFilter": true } }
    /// { "filter": true }
    /// </code>
    /// </summary>
    private static bool Evaluate(object? extra)
    {
        if (extra is null)
        {
            return false;
        }

        if (extra is bool flag)
        {
            return flag;
        }

        try
        {
            var json = JsonSerializer.Serialize(extra, DeEarthXJsonOptions.Default);
            using var doc = JsonDocument.Parse(json);
            return ReadFlag(doc.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReadFlag(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in new[] { "filter", "shouldFilter", "should_filter", "blocked" })
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed)
                {
                    return true;
                }
            }
        }

        // 允许嵌套在 result / decision 里。
        foreach (var wrapper in new[] { "result", "decision", "extra" })
        {
            if (element.TryGetProperty(wrapper, out var nested) && nested.ValueKind == JsonValueKind.Object && ReadFlag(nested))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 汇总“插件声明的过滤策略”与“插件通过扩展注册中心贡献的过滤策略”，
/// 两者使用同一套 <see cref="IFilterStrategy"/> 契约，宿主过滤引擎无需区分来源。
/// </summary>
public sealed class PluginFilterStrategyProvider
{
    private readonly PluginManager _pluginManager;
    private readonly IPluginHookExecutor _hookExecutor;
    private readonly PluginExtensionCatalog _extensions;

    public PluginFilterStrategyProvider(
        PluginManager pluginManager,
        IPluginHookExecutor hookExecutor,
        PluginExtensionCatalog extensions)
    {
        _pluginManager = pluginManager;
        _hookExecutor = hookExecutor;
        _extensions = extensions;
    }

    public async Task<List<IFilterStrategy>> GetFilterStrategiesAsync(CancellationToken ct = default)
    {
        var plugins = await _pluginManager.GetPluginsAsync(ct).ConfigureAwait(false);
        var strategies = new List<IFilterStrategy>();

        // 1) 清单声明式：filterStrategies + hooks 中的 filter_strategy。
        foreach (var plugin in plugins)
        {
            if (!plugin.Config.Enabled)
            {
                continue;
            }

            var declared = plugin.Manifest.Capabilities.FilterStrategies;
            if (declared.Count == 0)
            {
                continue;
            }

            foreach (var def in declared)
            {
                strategies.Add(new PluginFilterStrategyAdapter(plugin, def, _hookExecutor));
            }
        }

        // 2) 注册式：进程内插件通过 PluginContext.Extensions 注册的实现。
        foreach (var contribution in _extensions.GetAll<IFilterStrategy>(PluginExtensionKinds.FilterStrategy))
        {
            strategies.Add(contribution);
        }

        return strategies;
    }
}
