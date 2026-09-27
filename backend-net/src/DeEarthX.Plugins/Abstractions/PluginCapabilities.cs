using System.Text.Json.Serialization;

namespace DeEarthX.Plugins;

/// <summary>
/// 插件依赖声明。支持两种写法以兼容旧版清单：
/// <code>
/// "dependencies": ["a", "b"]
/// "dependencies": [{ "id": "a", "version": "&gt;=1.2.0", "optional": false }]
/// </code>
/// 旧版字符串数组会被解析为 <see cref="Optional"/> = false、<see cref="Version"/> = null。
/// </summary>
public sealed class PluginDependency
{
    public string Id { get; set; } = string.Empty;

    /// <summary>最低版本要求；null 表示不校验版本。</summary>
    public string? Version { get; set; }

    /// <summary>可选依赖：缺失时只记录警告，不阻止加载。</summary>
    public bool Optional { get; set; }
}

/// <summary>
/// 插件能力声明。旧的扁平字段（<c>sidebar</c> / <c>page</c> / <c>injectJS</c> / <c>injectCSS</c> / <c>main</c>）
/// 仍然可用：反序列化时若 <see cref="PluginManifest.Capabilities"/> 为空，则从旧字段回填；
/// 序列化时同时输出新旧两套字段，因此老版本读取者不受影响。
/// </summary>
public sealed class PluginCapabilities
{
    /// <summary>在侧边栏注册入口。</summary>
    public bool Sidebar { get; set; }

    /// <summary>提供可内嵌的 frontend 页面。</summary>
    public bool Pages { get; set; }

    /// <summary>注入到主界面的脚本（相对插件根目录）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> InjectScripts { get; set; } = new();

    /// <summary>注入到主界面的样式（相对插件根目录）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> InjectStyles { get; set; } = new();

    /// <summary>进程钩子入口文件（相对插件根目录）。</summary>
    public string? HookEntry { get; set; }

    /// <summary>声明的钩子名称，供宿主构建执行计划。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Hooks { get; set; } = new();

    /// <summary>参与模组过滤的扩展策略。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<PluginFilterStrategyDef> FilterStrategies { get; set; } = new();

    /// <summary>声明的权限点，宿主可据此限制插件能力。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string> Permissions { get; set; } = new();

    /// <summary>相对插件根目录的模板文件，供模板系统使用。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Templates { get; set; } = new();

    /// <summary>相对插件根目录的脚本资源。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Scripts { get; set; } = new();

    [JsonIgnore]
    public bool HasAnyInjection => InjectScripts.Count > 0 || InjectStyles.Count > 0;

    [JsonIgnore]
    public bool HasHooks => Hooks.Count > 0 && !string.IsNullOrWhiteSpace(HookEntry);

    internal PluginCapabilities Clone()
    {
        return new PluginCapabilities
        {
            Sidebar = Sidebar,
            Pages = Pages,
            InjectScripts = new List<string>(InjectScripts),
            InjectStyles = new List<string>(InjectStyles),
            HookEntry = HookEntry,
            Hooks = new List<string>(Hooks),
            FilterStrategies = new List<PluginFilterStrategyDef>(FilterStrategies),
            Permissions = new Dictionary<string, string>(Permissions, StringComparer.Ordinal),
            Templates = new List<string>(Templates),
            Scripts = new List<string>(Scripts)
        };
    }
}
