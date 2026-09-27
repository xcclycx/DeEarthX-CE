using System.Text.Json.Nodes;

namespace DeEarthX.Plugins;

/// <summary>
/// 清单与配置校验。
/// <para>
/// 除了内置规则，还会执行所有通过 <see cref="PluginExtensionKinds.ManifestValidator"/>
/// 注册的扩展校验器，因此第三方可以追加自己的约束而无需修改本类。
/// </para>
/// </summary>
public sealed class PluginValidator
{
    private readonly PluginExtensionCatalog _extensions;

    public PluginValidator(PluginExtensionCatalog extensions) => _extensions = extensions;

    public PluginValidationResult Validate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Id))
            errors.Add("插件 ID 不能为空");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("插件名称不能为空");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("插件版本不能为空");
        if (string.IsNullOrWhiteSpace(manifest.Author))
            warnings.Add("建议填写插件作者");

        if (manifest.Id?.Length > 50)
            errors.Add("插件 ID 长度不能超过 50 个字符");
        if (manifest.Name?.Length > 100)
            errors.Add("插件名称长度不能超过 100 个字符");
        if (manifest.Version?.Contains(' ') == true)
            errors.Add("插件版本不能包含空格");

        if (manifest.Id is { Length: > 0 } id && id != PluginManager.SanitizeId(id))
            errors.Add("插件 ID 只能包含字母、数字、下划线、短横线与点");

        if (manifest.Dependencies.Count > 50)
            warnings.Add("插件依赖过多，建议控制在 50 个以内");

        if (manifest.Dependencies.Any(d => string.IsNullOrWhiteSpace(d.Id)))
            errors.Add("依赖声明缺少 id");

        if (manifest.Dependencies.Any(d => string.Equals(d.Id, manifest.Id, StringComparison.Ordinal)))
            errors.Add("插件不能依赖自身");

        foreach (var permission in manifest.Capabilities.Permissions)
        {
            if (string.IsNullOrWhiteSpace(permission.Key) || string.IsNullOrWhiteSpace(permission.Value))
                errors.Add($"权限声明不完整: {permission.Key}");
        }

        foreach (var strategy in manifest.Capabilities.FilterStrategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.Name))
                errors.Add("过滤策略名称不能为空");
        }

        // 声明了钩子却没有入口，是插件作者最常见的错误，直接给出可操作的提示。
        var capabilities = manifest.Capabilities;
        if (capabilities.Hooks.Count > 0 && string.IsNullOrWhiteSpace(capabilities.HookEntry))
        {
            errors.Add("声明了 hooks 但未设置 main/entryPoint 入口文件");
        }
        else if (!string.IsNullOrWhiteSpace(capabilities.HookEntry) && capabilities.Hooks.Count == 0)
        {
            warnings.Add("设置了入口文件但未声明 hooks，插件不会参与任何流水线");
        }

        if (capabilities.HookEntry is { Length: > 0 } entry && Path.IsPathRooted(entry))
        {
            errors.Add("main/entryPoint 必须是插件目录内的相对路径");
        }

        if (capabilities.Sidebar && manifest.SidebarItems is not { Count: > 0 })
        {
            warnings.Add("声明了 sidebar 但未提供 sidebarItems，侧边栏不会出现入口");
        }

        foreach (var item in manifest.SidebarItems ?? new List<PluginSidebarItem>())
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Route))
                errors.Add("sidebarItems 的 key 与 route 不能为空");
        }

        if (manifest.Lifecycle is { } lifecycle)
        {
            if (lifecycle.Start is { Length: > 0 } start && Path.IsPathRooted(start))
                errors.Add("lifecycle.start 必须是插件目录内的相对路径");
            if (lifecycle.Stop is { Length: > 0 } stop && Path.IsPathRooted(stop))
                errors.Add("lifecycle.stop 必须是插件目录内的相对路径");
        }

        var result = new PluginValidationResult { Errors = errors, Warnings = warnings };

        foreach (var validator in _extensions.GetAll<IPluginManifestValidator>(PluginExtensionKinds.ManifestValidator))
        {
            try
            {
                validator.Validate(manifest, result);
            }
            catch (Exception ex)
            {
                warnings.Add($"扩展校验器 {validator.Name} 执行失败: {ex.Message}");
            }
        }

        result.IsValid = errors.Count == 0;
        return result;
    }

    public PluginValidationResult ValidateConfig(PluginManifest manifest, PluginConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (config.Id != manifest.Id)
            errors.Add("配置 ID 与插件清单 ID 不匹配");

        if (manifest.DefaultConfig is JsonObject defaults)
        {
            foreach (var kv in defaults)
            {
                if (!config.Settings.ContainsKey(kv.Key))
                {
                    warnings.Add($"配置中缺少默认设置项: {kv.Key}");
                }
            }
        }

        return new PluginValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    /// <summary>
    /// 生成配置项 UI 描述：先由扩展点提供方描述，再由内置的 <c>defaultConfig</c> 推断补齐。
    /// 前端据此渲染下拉框、密码框、数值范围等控件。
    /// </summary>
    public IReadOnlyList<PluginSettingField> DescribeSettings(PluginManifest manifest)
    {
        var merged = new Dictionary<string, PluginSettingField>(StringComparer.Ordinal);

        foreach (var provider in _extensions.GetAll<IPluginSettingsSchemaProvider>(PluginExtensionKinds.SettingsSchema))
        {
            IReadOnlyDictionary<string, PluginSettingField> described;
            try
            {
                described = provider.Describe(manifest);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var kv in described)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        // 内置回退：按 defaultConfig 的值类型推断控件，保证任何插件都有可渲染的配置页。
        var inferred = new DefaultSettingsSchemaProvider().Describe(manifest);
        foreach (var kv in inferred)
        {
            if (!merged.ContainsKey(kv.Key))
            {
                merged[kv.Key] = kv.Value;
            }
        }

        // 清单里的 configLabels / configDescriptions 属于作者意图，优先级最高。
        foreach (var kv in merged)
        {
            if (manifest.ConfigLabels is not null && manifest.ConfigLabels.TryGetValue(kv.Key, out var label))
            {
                kv.Value.Label = label;
            }

            if (manifest.ConfigDescriptions is not null && manifest.ConfigDescriptions.TryGetValue(kv.Key, out var description))
            {
                kv.Value.Description = description;
            }
        }

        return merged.Values
            .OrderBy(f => f.Order ?? int.MaxValue)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
    }
}

public sealed class PluginValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
