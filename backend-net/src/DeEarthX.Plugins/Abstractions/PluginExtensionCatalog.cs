using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

/// <summary>
/// 扩展点贡献的统一注册中心。
/// <para>
/// 宿主只定义“扩展点种类”（kind，字符串）与贡献对象的接口，插件通过
/// <see cref="Register{T}"/> 注册实现，消费方通过 <see cref="GetAll{T}"/> 读取。
/// 新增一种扩展点不需要修改任何既有类型 —— 这是插件系统可扩展性的核心。
/// </para>
/// </summary>
public sealed class PluginExtensionCatalog
{
    private readonly ConcurrentDictionary<Type, IRegistry> _registries = new();
    private readonly ILogService _log;

    public PluginExtensionCatalog(ILogService log) => _log = log;

    /// <summary>注册一个贡献。同一插件重复注册同一 (kind, key) 会覆盖，避免重载后重复。</summary>
    public IDisposable Register<T>(string kind, string key, T contribution, string ownerPluginId, int priority = 0)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contribution);

        var registry = (Registry<T>)_registries.GetOrAdd(typeof(T), _ => new Registry<T>());
        var registration = new Contribution<T>(key, kind, contribution, ownerPluginId, priority);
        registry.Add(registration);

        return new Registration(() => registry.Remove(registration));
    }

    /// <summary>按 kind 读取贡献，按优先级降序。</summary>
    public IReadOnlyList<T> GetAll<T>(string? kind = null) where T : class
    {
        if (!_registries.TryGetValue(typeof(T), out var registry))
        {
            return Array.Empty<T>();
        }

        return ((Registry<T>)registry).Snapshot(kind);
    }

    /// <summary>移除某个插件贡献的全部扩展（禁用/卸载时调用）。</summary>
    public void RemoveOwner(string ownerPluginId)
    {
        foreach (var registry in _registries.Values)
        {
            registry.RemoveOwner(ownerPluginId);
        }
    }

    /// <summary>已注册的扩展点种类及其贡献数量，用于诊断接口。</summary>
    public IReadOnlyList<PluginExtensionPointInfo> Describe()
    {
        var result = new List<PluginExtensionPointInfo>();
        foreach (var registry in _registries.Values)
        {
            result.AddRange(registry.Describe());
        }

        return result
            .OrderBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
    }

    private interface IRegistry
    {
        void RemoveOwner(string ownerPluginId);

        IReadOnlyList<PluginExtensionPointInfo> Describe();
    }

    private sealed class Registry<T> : IRegistry where T : class
    {
        private readonly object _gate = new();
        private readonly List<Contribution<T>> _items = new();

        public void Add(Contribution<T> contribution)
        {
            lock (_gate)
            {
                _items.RemoveAll(c => c.Kind == contribution.Kind && c.Key == contribution.Key);
                _items.Add(contribution);
                _items.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public void Remove(Contribution<T> contribution)
        {
            lock (_gate)
            {
                _items.Remove(contribution);
            }
        }

        public void RemoveOwner(string ownerPluginId)
        {
            lock (_gate)
            {
                _items.RemoveAll(c => c.OwnerPluginId == ownerPluginId);
            }
        }

        public IReadOnlyList<T> Snapshot(string? kind)
        {
            lock (_gate)
            {
                return _items
                    .Where(c => kind is null || c.Kind == kind)
                    .Select(c => c.Value)
                    .ToList();
            }
        }

        public IReadOnlyList<PluginExtensionPointInfo> Describe()
        {
            lock (_gate)
            {
                return _items
                    .GroupBy(c => c.Kind, StringComparer.Ordinal)
                    .Select(g => new PluginExtensionPointInfo(
                        typeof(T).Name,
                        g.Key,
                        g.Count(),
                        g.Select(c => c.OwnerPluginId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()))
                    .ToList();
            }
        }
    }

    private sealed record Contribution<T>(string Key, string Kind, T Value, string OwnerPluginId, int Priority) where T : class;

    private sealed class Registration : IDisposable
    {
        private Action? _dispose;

        public Registration(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

/// <summary>扩展点种类诊断信息。</summary>
public sealed record PluginExtensionPointInfo(string Type, string Kind, int Count, IReadOnlyList<string> Owners);

/// <summary>宿主内置的扩展点种类常量。</summary>
public static class PluginExtensionKinds
{
    /// <summary>贡献 <c>DeEarthX.Core.Filter.IFilterStrategy</c>。</summary>
    public const string FilterStrategy = "filter-strategy";

    /// <summary>贡献 <c>IPluginManifestValidator</c>，追加自定义清单校验。</summary>
    public const string ManifestValidator = "manifest-validator";

    /// <summary>贡献 <c>IPluginEventBus</c> 处理器时使用的种类标记。</summary>
    public const string EventHandler = "event-handler";

    /// <summary>贡献 <c>IPluginSettingsSchemaProvider</c>，为插件配置页提供结构化描述。</summary>
    public const string SettingsSchema = "settings-schema";
}

/// <summary>
/// 清单校验扩展点：插件或首方模块可追加自己的校验规则，
/// 例如“声明了 hooks 就必须存在 HookEntry”。
/// </summary>
public interface IPluginManifestValidator
{
    string Name { get; }

    void Validate(PluginManifest manifest, PluginValidationResult result);
}

/// <summary>
/// 配置页描述扩展点：让 <c>defaultConfig</c> 从“键值猜测”升级为显式声明，
/// 前端可据此渲染下拉框、密码框、多行文本等控件。
/// </summary>
public interface IPluginSettingsSchemaProvider
{
    string Name { get; }

    /// <summary>返回配置项描述，键为配置键名。</summary>
    IReadOnlyDictionary<string, PluginSettingField> Describe(PluginManifest manifest);
}

/// <summary>单个配置项的 UI 描述。</summary>
public sealed class PluginSettingField
{
    public string Key { get; set; } = string.Empty;

    /// <summary>控件类型：string / number / boolean / select / password / textarea。</summary>
    public string Type { get; set; } = "string";

    public string? Label { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; }

    public JsonNode? Default { get; set; }

    public List<PluginSettingOption>? Options { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    public int? Order { get; set; }

    public PluginSettingField Clone() => new()
    {
        Key = Key,
        Type = Type,
        Label = Label,
        Description = Description,
        Required = Required,
        Default = Default?.DeepClone(),
        Options = Options?.Select(o => new PluginSettingOption { Value = o.Value, Label = o.Label }).ToList(),
        Min = Min,
        Max = Max,
        Order = Order
    };
}

public sealed class PluginSettingOption
{
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

/// <summary>把 <c>defaultConfig</c> 推断成结构化配置描述的内置实现。</summary>
public sealed class DefaultSettingsSchemaProvider : IPluginSettingsSchemaProvider
{
    public string Name => "builtin:default-config";

    public IReadOnlyDictionary<string, PluginSettingField> Describe(PluginManifest manifest)
    {
        var fields = new Dictionary<string, PluginSettingField>(StringComparer.Ordinal);
        if (manifest.DefaultConfig is not JsonObject obj)
        {
            return fields;
        }

        var order = 0;
        foreach (var kv in obj)
        {
            var field = new PluginSettingField
            {
                Key = kv.Key,
                Default = kv.Value?.DeepClone(),
                Order = order++
            };

            if (manifest.ConfigLabels is not null && manifest.ConfigLabels.TryGetValue(kv.Key, out var label))
            {
                field.Label = label;
            }

            if (manifest.ConfigDescriptions is not null && manifest.ConfigDescriptions.TryGetValue(kv.Key, out var description))
            {
                field.Description = description;
            }

            field.Type = kv.Value switch
            {
                null => "string",
                JsonArray => "list",
                JsonObject => "object",
                _ => kv.Value.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
                    System.Text.Json.JsonValueKind.Number => "number",
                    _ => "string"
                }
            };

            fields[kv.Key] = field;
        }

        return fields;
    }
}
