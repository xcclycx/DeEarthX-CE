using System.Text.Json;
using System.Text.Json.Nodes;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

/// <summary>
/// 插件在宿主内的受控运行上下文。宿主通过它向插件开放日志、事件总线与配置读写，
/// 插件不需要（也不应）直接接触文件系统以外的宿主服务。
/// </summary>
public sealed class PluginContext
{
    private readonly ILogService _log;
    private readonly object _configGate = new();

    internal PluginContext(
        PluginManifest manifest,
        PluginConfig config,
        string directoryPath,
        ILogService log,
        IPluginEventBus events,
        PluginExtensionCatalog extensions)
    {
        Manifest = manifest;
        Config = config;
        DirectoryPath = directoryPath;
        Events = events;
        Extensions = extensions;
        _log = log;
    }

    public PluginManifest Manifest { get; }

    public PluginConfig Config { get; }

    public string DirectoryPath { get; }

    public string Id => Manifest.Id;

    /// <summary>订阅/发布宿主事件，用于插件间解耦通信。</summary>
    public IPluginEventBus Events { get; }

    /// <summary>向宿主注册扩展贡献（过滤策略、校验器、钩子等）。</summary>
    public PluginExtensionCatalog Extensions { get; }

    /// <summary>解析插件目录内的相对路径，并阻止越权访问。</summary>
    public string ResolvePath(string relativePath) => PluginPaths.ResolveInside(DirectoryPath, relativePath);

    /// <summary>读取插件目录内的相对路径，并阻止越权访问。</summary>
    public string ResolvePath(params string[] segments) => PluginPaths.ResolveInside(DirectoryPath, segments);

    public bool TryGetSetting(string key, out JsonNode? value)
    {
        lock (_configGate)
        {
            return Config.Settings.TryGetValue(key, out value);
        }
    }

    public T? GetSetting<T>(string key, T? fallback = default)
    {
        JsonNode? node;
        lock (_configGate)
        {
            if (!Config.Settings.TryGetValue(key, out node) || node is null)
            {
                return fallback;
            }
        }

        try
        {
            return node.Deserialize<T>(DeEarthXJsonOptions.Default) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public void SetSetting(string key, JsonNode? value)
    {
        lock (_configGate)
        {
            Config.Settings[key] = value;
        }
    }

    /// <summary>快照当前配置，避免调用方持有可变引用。</summary>
    public IReadOnlyDictionary<string, JsonNode?> SnapshotSettings()
    {
        lock (_configGate)
        {
            return new Dictionary<string, JsonNode?>(Config.Settings, StringComparer.Ordinal);
        }
    }

    public void Debug(string message, object? meta = null) => _log.Debug($"[{Id}] {message}", meta);

    public void Info(string message, object? meta = null) => _log.Info($"[{Id}] {message}", meta);

    public void Warn(string message, object? meta = null) => _log.Warn($"[{Id}] {message}", meta);

    public void Error(string message, object? meta = null) => _log.Error($"[{Id}] {message}", meta);

    public void Error(string message, Exception ex) => _log.Error($"[{Id}] {message}", ex);
}

/// <summary>插件目录路径工具：统一越权校验，避免各处各写一份。</summary>
public static class PluginPaths
{
    public static string ResolveInside(string rootDir, string relativePath)
        => ResolveInside(rootDir, new[] { relativePath });

    public static string ResolveInside(string rootDir, params string[] segments)
    {
        var root = Path.GetFullPath(rootDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var parts = segments.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        var combined = Path.Combine(new[] { root }.Concat(parts).ToArray());
        var resolved = Path.GetFullPath(combined);

        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolved, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("路径越权访问被拒绝");
        }

        return resolved;
    }
}
