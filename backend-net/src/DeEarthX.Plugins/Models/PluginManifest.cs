using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeEarthX.Plugins;

/// <summary>
/// 插件清单（<c>manifest.json</c>）。
/// <para>
/// 设计约定：新增能力一律进入分组属性（<see cref="Capabilities"/>、<see cref="Lifecycle"/>…），
/// 同时保留旧版扁平字段作为别名属性。别名属性的 getter 读取新模型、setter 回写新模型，
/// 并且新旧字段都会出现在序列化结果中 —— 因此<b>旧插件清单继续可用，旧版宿主也能读新清单</b>。
/// </para>
/// </summary>
public sealed class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }

    /// <summary>主页/仓库地址。别名为旧字段 <c>url</c>。</summary>
    public string? Homepage { get; set; }

    public string? License { get; set; }
    public string? Icon { get; set; }

    /// <summary>要求的最低宿主版本。</summary>
    public string? MinAppVersion { get; set; }

    /// <summary>是否为开源插件（供插件市场展示）。</summary>
    public bool? OpenSource { get; set; }

    /// <summary>源码地址。</summary>
    public string? SourceUrl { get; set; }

    /// <summary>插件包格式版本，缺省视为 1。宿主可据此做迁移。</summary>
    public int ManifestVersion { get; set; } = 1;

    // ---------------------------------------------------------------- 能力

    public PluginCapabilities Capabilities { get; set; } = new();

    public PluginLifecycleScripts? Lifecycle { get; set; }

    // ---------------------------------------------------------------- 元数据

    /// <summary>依赖声明，支持字符串数组与对象数组两种写法。</summary>
    [JsonConverter(typeof(PluginDependencyListConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<PluginDependency> Dependencies { get; set; } = new();

    /// <summary>侧边栏条目。为空的条目会被省略；旧字段 <c>hasSidebar</c> 仍会输出，旧前端不受影响。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginSidebarItem>? SidebarItems { get; set; }

    public JsonNode? DefaultConfig { get; set; }

    /// <summary>配置项显示名（键 → 标签）。</summary>
    public Dictionary<string, string>? ConfigLabels { get; set; }

    /// <summary>配置项说明文本（键 → 描述）。</summary>
    public Dictionary<string, string>? ConfigDescriptions { get; set; }

    /// <summary>插件启用时是否立即参与钩子流水线。false 时仅在显式调用时执行。</summary>
    public bool AutoStart { get; set; } = true;

    public PluginResources? Resources { get; set; }

    // ------------------------------------------------- 旧版扁平字段（别名）

    /// <summary>旧字段：进程钩子入口。读写映射到 <see cref="PluginCapabilities.HookEntry"/>。</summary>
    public string? Main
    {
        get => Capabilities.HookEntry;
        set => Capabilities.HookEntry = value;
    }

    /// <summary>旧字段：入口别名，读写映射到 <see cref="PluginCapabilities.HookEntry"/>。</summary>
    public string? EntryPoint
    {
        get => Capabilities.HookEntry;
        set => Capabilities.HookEntry = value;
    }

    /// <summary>旧字段：声明在侧边栏注册。读写映射到 <see cref="PluginCapabilities.Sidebar"/>。</summary>
    public bool Sidebar
    {
        get => Capabilities.Sidebar;
        set => Capabilities.Sidebar = value;
    }

    /// <summary>旧字段：声明提供页面。读写映射到 <see cref="PluginCapabilities.Pages"/>。</summary>
    public bool Page
    {
        get => Capabilities.Pages;
        set => Capabilities.Pages = value;
    }

    /// <summary>旧字段：钩子名称列表。读写映射到 <see cref="PluginCapabilities.Hooks"/>。</summary>
    public List<string> Hooks
    {
        get => Capabilities.Hooks;
        set => Capabilities.Hooks = value ?? new List<string>();
    }

    /// <summary>旧字段：过滤策略。读写映射到 <see cref="PluginCapabilities.FilterStrategies"/>。</summary>
    public List<PluginFilterStrategyDef> FilterStrategies
    {
        get => Capabilities.FilterStrategies;
        set => Capabilities.FilterStrategies = value ?? new List<PluginFilterStrategyDef>();
    }

    /// <summary>旧字段：权限声明。读写映射到 <see cref="PluginCapabilities.Permissions"/>。</summary>
    public Dictionary<string, string> Permissions
    {
        get => Capabilities.Permissions;
        set => Capabilities.Permissions = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    [JsonPropertyName("injectJS")]
    public List<string> InjectJS
    {
        get => Capabilities.InjectScripts;
        set => Capabilities.InjectScripts = value ?? new List<string>();
    }

    [JsonPropertyName("injectCSS")]
    public List<string> InjectCSS
    {
        get => Capabilities.InjectStyles;
        set => Capabilities.InjectStyles = value ?? new List<string>();
    }

    /// <summary>
    /// 旧字段：宿主未识别的注入资源列表。保留以便往返序列化不丢字段。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Injects { get; set; } = new();

    // ---------------------------------------------------------------- 行为

    /// <summary>
    /// 反序列化后归一化：
    /// <list type="bullet">
    ///   <item>若只写了旧字段，把它们搬进 <see cref="Capabilities"/>；</item>
    ///   <item>若只写了新字段，把它们回填到旧别名（别名属性本身是映射，无需处理）；</item>
    ///   <item>补齐 <c>hooks</c> 与 <c>hookEntry</c> 的一致性。</item>
    /// </list>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNormalized { get; private set; }

    public PluginManifest Normalize()
    {
        Capabilities ??= new PluginCapabilities();

        if (Capabilities.InjectScripts is null) Capabilities.InjectScripts = new List<string>();
        if (Capabilities.InjectStyles is null) Capabilities.InjectStyles = new List<string>();
        if (Capabilities.Hooks is null) Capabilities.Hooks = new List<string>();
        if (Capabilities.FilterStrategies is null) Capabilities.FilterStrategies = new List<PluginFilterStrategyDef>();
        if (Capabilities.Permissions is null) Capabilities.Permissions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Capabilities.Templates is null) Capabilities.Templates = new List<string>();
        if (Capabilities.Scripts is null) Capabilities.Scripts = new List<string>();

        // 旧清单可能把注入写成 "injects": ["a.js"]，这里补进 injectJS。
        if (Capabilities.InjectScripts.Count == 0 && Injects.Count > 0)
        {
            Capabilities.InjectScripts.AddRange(Injects);
        }

        // 旧清单可能只声明 hooks 而用 main 之外的默认入口，这里补齐资源声明。
        if (Resources is not null)
        {
            if (Resources.Scripts is { Count: > 0 } && Capabilities.Scripts.Count == 0)
            {
                Capabilities.Scripts.AddRange(Resources.Scripts);
            }

            if (Resources.Templates is { Count: > 0 } && Capabilities.Templates.Count == 0)
            {
                Capabilities.Templates.AddRange(Resources.Templates);
            }
        }

        Dependencies ??= new List<PluginDependency>();
        Injects ??= new List<string>();

        // 去重并保持声明顺序，避免宿主构建执行计划时重复执行同一钩子。
        Capabilities.Hooks = Dedupe(Capabilities.Hooks);
        Capabilities.InjectScripts = Dedupe(Capabilities.InjectScripts);
        Capabilities.InjectStyles = Dedupe(Capabilities.InjectStyles);

        IsNormalized = true;
        return this;
    }

    private static List<string> Dedupe(List<string> source)
    {
        if (source.Count <= 1)
        {
            return source;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(source.Count);
        foreach (var item in source)
        {
            if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    internal PluginManifest Clone()
    {
        return new PluginManifest
        {
            Id = Id,
            Name = Name,
            Version = Version,
            Description = Description,
            Author = Author,
            Homepage = Homepage,
            License = License,
            Icon = Icon,
            MinAppVersion = MinAppVersion,
            OpenSource = OpenSource,
            SourceUrl = SourceUrl,
            ManifestVersion = ManifestVersion,
            Capabilities = Capabilities.Clone(),
            Lifecycle = Lifecycle is null ? null : new PluginLifecycleScripts { Start = Lifecycle.Start, Stop = Lifecycle.Stop },
            Dependencies = Dependencies.Select(d => new PluginDependency { Id = d.Id, Version = d.Version, Optional = d.Optional }).ToList(),
            SidebarItems = SidebarItems?.Select(i => new PluginSidebarItem { Key = i.Key, Label = i.Label, Icon = i.Icon, Route = i.Route }).ToList(),
            DefaultConfig = DefaultConfig?.DeepClone(),
            ConfigLabels = ConfigLabels is null ? null : new Dictionary<string, string>(ConfigLabels, StringComparer.Ordinal),
            ConfigDescriptions = ConfigDescriptions is null ? null : new Dictionary<string, string>(ConfigDescriptions, StringComparer.Ordinal),
            AutoStart = AutoStart,
            Resources = Resources is null ? null : new PluginResources
            {
                Files = Resources.Files is null ? null : new List<string>(Resources.Files),
                Templates = Resources.Templates is null ? null : new List<string>(Resources.Templates),
                Scripts = Resources.Scripts is null ? null : new List<string>(Resources.Scripts)
            },
            Injects = new List<string>(Injects)
        };
    }

    /// <summary>兼容旧字段 <c>url</c> 的读写（前端与旧插件包使用该名称）。</summary>
    [JsonPropertyName("url")]
    public string? Url
    {
        get => Homepage;
        set => Homepage = value;
    }

    /// <summary>兼容旧字段 <c>hasSidebar</c> 的读写。</summary>
    [JsonPropertyName("hasSidebar")]
    public bool HasSidebar
    {
        get => Capabilities.Sidebar;
        set => Capabilities.Sidebar = value;
    }
}

public sealed class PluginSidebarItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string Route { get; set; } = string.Empty;
}
