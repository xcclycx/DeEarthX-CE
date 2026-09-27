using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeEarthX.Plugins;

public sealed class PluginConfig
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>是否由宿主自动启动并参与钩子流水线。与 <see cref="Enabled"/> 分离，便于“已启用但暂停”场景。</summary>
    public bool AutoStart { get; set; } = true;

    public Dictionary<string, JsonNode?> Settings { get; set; } = new(StringComparer.Ordinal);

    public PluginConfig Clone() => new()
    {
        Id = Id,
        Enabled = Enabled,
        AutoStart = AutoStart,
        Settings = Settings.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone(), StringComparer.Ordinal)
    };
}

public sealed record PluginFilterStrategyDef(
    string Name,
    string? Type,
    string? Label,
    string? Hook);

/// <summary>插件的加载状态，用于诊断接口与前端展示。</summary>
public enum PluginLoadState
{
    /// <summary>清单与依赖均正常。</summary>
    Ready = 0,

    /// <summary>缺少依赖，已跳过加载。</summary>
    DependencyMissing = 1,

    /// <summary>依赖版本不满足。</summary>
    DependencyVersionMismatch = 2,

    /// <summary>依赖成环。</summary>
    DependencyCycle = 3,

    /// <summary>清单解析或校验失败。</summary>
    Invalid = 4,

    /// <summary>加载过程抛出异常（I/O 等）。</summary>
    Failed = 5
}

/// <summary>
/// 一个已装入宿主的插件。除 <see cref="Manifest"/> 与 <see cref="Config"/> 外，
/// 其余字段都是宿主内部状态，不会出现在 <c>/plugins</c> 的 JSON 响应里。
/// </summary>
public sealed class LoadedPlugin
{
    public required PluginManifest Manifest { get; init; }

    public required PluginConfig Config { get; set; }

    [JsonIgnore]
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>插件运行上下文（仅进程内使用，绝不序列化）。</summary>
    [JsonIgnore]
    public PluginContext? Context { get; init; }

    [JsonIgnore]
    public PluginLoadState State { get; init; } = PluginLoadState.Ready;

    [JsonIgnore]
    public string? LoadError { get; init; }

    /// <summary>按依赖优先顺序排列的依赖 id（拓扑序），用于展示与重载。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ResolvedDependencies { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public bool IsEnabled => Config.Enabled;

    /// <summary>是否应参与钩子流水线：已启用、已请求自启、且声明了钩子入口。</summary>
    [JsonIgnore]
    public bool CanRunHooks => Config.Enabled && Config.AutoStart && Manifest.Capabilities.HasHooks;

    public override string ToString() => $"{Manifest.Name} ({Manifest.Id}) v{Manifest.Version}";
}
