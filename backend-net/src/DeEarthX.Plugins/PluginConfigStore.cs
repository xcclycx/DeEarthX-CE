using System.Text.Json;
using System.Text.Json.Nodes;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

public interface IPluginConfigStore
{
    /// <summary>读取全局配置快照（一次磁盘读取，结果被缓存）。</summary>
    Task<IReadOnlyDictionary<string, PluginConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>取得某个插件的配置，缺失时用清单默认值创建并落盘。</summary>
    Task<PluginConfig> GetOrCreateAsync(PluginManifest manifest, string pluginDirectory, CancellationToken ct = default);

    /// <summary>保存单个插件配置。</summary>
    Task SaveAsync(string pluginId, PluginConfig config, CancellationToken ct = default);

    /// <summary>移除某个插件的配置条目。</summary>
    Task RemoveAsync(string pluginId, CancellationToken ct = default);

    /// <summary>使缓存失效，下次读取重新落盘（外部改动插件目录后调用）。</summary>
    void Invalidate();
}

/// <summary>
/// 插件配置持久化。
/// <para>
/// 与旧实现的关键差异：
/// <list type="number">
///   <item>所有插件配置合并到<b>单个</b> <c>plugin-configs.json</c>，启动时只读一次磁盘，
///         而不是每个插件读一次（旧实现为 N 次 IO + N 次反序列化）；</item>
///   <item>写入是<b>合并去抖</b>的：连续调用只落盘一次，且通过临时文件 + 原子替换避免写坏；</item>
///   <item>首次读取旧的 <c>plugins/&lt;id&gt;/config.json</c> 会自动迁移，用户数据不丢。</item>
/// </list>
/// </para>
/// </summary>
public sealed class PluginConfigStore : IPluginConfigStore, IDisposable
{
    private const string LegacyGlobalFileName = "plugin-configs.json";
    private const string LegacyPerPluginFileName = "config.json";

    private readonly string _pluginsDir;
    private readonly string _storePath;
    private readonly ILogService _log;
    private readonly JsonSerializerOptions _json = DeEarthXJsonOptions.Default;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, PluginConfig>? _cache;
    private bool _dirty;
    private bool _disposed;

    public PluginConfigStore(IAppDirectoryProvider appDirectoryProvider, ILogService log)
    {
        var appDir = appDirectoryProvider.GetAppDirectory();
        _pluginsDir = Path.Combine(appDir, "plugins");
        _storePath = Path.Combine(appDir, LegacyGlobalFileName);
        _log = log;
    }

    public async Task<IReadOnlyDictionary<string, PluginConfig>> GetAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return new Dictionary<string, PluginConfig>(await LoadLockedAsync(ct).ConfigureAwait(false), StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginConfig> GetOrCreateAsync(PluginManifest manifest, string pluginDirectory, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await LoadLockedAsync(ct).ConfigureAwait(false);
            if (map.TryGetValue(manifest.Id, out var existing))
            {
                ApplyDefaults(existing, manifest);
                ApplyManifestFlags(existing, manifest);
                existing.Id = manifest.Id;
                return existing;
            }

            var config = new PluginConfig { Id = manifest.Id, Enabled = true, AutoStart = manifest.AutoStart };
            ApplyDefaults(config, manifest, overwrite: false);
            ApplyManifestFlags(config, manifest);

            // 首次接管旧版 per-plugin config.json，保留用户已有设置。
            var legacyPath = Path.Combine(pluginDirectory, LegacyPerPluginFileName);
            if (File.Exists(legacyPath))
            {
                try
                {
                    var legacyJson = await File.ReadAllTextAsync(legacyPath, ct).ConfigureAwait(false);
                    var legacy = JsonSerializer.Deserialize<PluginConfig>(legacyJson, _json);
                    if (legacy is not null)
                    {
                        config.Enabled = legacy.Enabled;
                        foreach (var kv in legacy.Settings)
                        {
                            config.Settings[kv.Key] = kv.Value;
                        }

                        _log.Info($"已迁移旧版插件配置: {manifest.Id}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"读取旧版插件配置失败，使用默认值: {manifest.Id} ({ex.Message})");
                }
            }

            map[manifest.Id] = config;
            MarkDirtyLocked();
            return config;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string pluginId, PluginConfig config, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await LoadLockedAsync(ct).ConfigureAwait(false);
            config.Id = pluginId;
            map[pluginId] = config;
            MarkDirtyLocked();
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string pluginId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await LoadLockedAsync(ct).ConfigureAwait(false);
            if (map.Remove(pluginId))
            {
                MarkDirtyLocked();
            }
            else
            {
                return;
            }
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(ct).ConfigureAwait(false);
    }

    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _cache = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PluginConfig>> LoadLockedAsync(CancellationToken ct)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var map = new Dictionary<string, PluginConfig>(StringComparer.Ordinal);

        if (File.Exists(_storePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_storePath, ct).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json, _json);
                if (loaded is not null)
                {
                    foreach (var kv in loaded)
                    {
                        var config = ConvertEntry(kv.Key, kv.Value);
                        if (config is null)
                        {
                            continue;
                        }

                        config.Id = kv.Key;
                        config.Settings ??= new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
                        map[kv.Key] = config;

                        // 旧版是 {"<id>":"true"} 布尔映射，升格后需要回写为富格式。
                        _dirty |= kv.Value is not JsonObject;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"插件配置读取失败，将使用默认值重建: {ex.Message}");
            }
        }

        _cache = map;
        return map;
    }

    /// <summary>
    /// 把存储中的一个条目转成 <see cref="PluginConfig"/>，同时兼容两代历史格式：
    /// <list type="bullet">
    ///   <item>新版富格式：<c>"&lt;id&gt;": { "enabled": true, "settings": { … } }</c>；</item>
    ///   <item>旧版布尔映射：<c>"&lt;id&gt;": "true"</c>（设置项当时存在各插件的 config.json 里）。</item>
    /// </list>
    /// 旧版条目会被就地升级，避免用户已有的启用状态丢失。
    /// </summary>
    private static PluginConfig? ConvertEntry(string id, JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            try
            {
                return obj.Deserialize<PluginConfig>(DeEarthXJsonOptions.Default) ?? new PluginConfig { Id = id };
            }
            catch (JsonException)
            {
                return new PluginConfig { Id = id };
            }
        }

        var text = node.ToString();
        if (bool.TryParse(text, out var enabled))
        {
            return new PluginConfig { Id = id, Enabled = enabled, AutoStart = enabled };
        }

        return new PluginConfig { Id = id };
    }

    /// <summary>清单里的默认值只补缺失键，不覆盖用户已保存的值。</summary>
    private static void ApplyDefaults(PluginConfig config, PluginManifest manifest, bool overwrite = false)
    {
        if (manifest.DefaultConfig is not JsonObject defaults)
        {
            return;
        }

        foreach (var kv in defaults)
        {
            if (overwrite)
            {
                config.Settings[kv.Key] = kv.Value?.DeepClone();
            }
            else if (!config.Settings.ContainsKey(kv.Key))
            {
                config.Settings[kv.Key] = kv.Value?.DeepClone();
            }
        }
    }

    private static void ApplyManifestFlags(PluginConfig config, PluginManifest manifest)
    {
        // 清单显式声明 autoStart: false 时以清单为准，避免旧配置把插件强行拉起。
        if (!manifest.AutoStart)
        {
            config.AutoStart = false;
        }
    }

    private void MarkDirtyLocked()
    {
        _dirty = true;
    }

    /// <summary>
    /// 合并并发写入：同一时刻最多一次落盘，且后到的修改会合进同一次写入。
    /// 写盘在 <see cref="_gate"/> 内完成（配置对象只增不减、序列化开销极小），
    /// 既保证原子性，也避免了嵌套加锁导致的自死锁。
    /// </summary>
    private async Task FlushAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_dirty || _cache is null)
            {
                return;
            }

            _dirty = false;
            var json = JsonSerializer.Serialize(_cache, _json);
            await AtomicWriteAsync(_storePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // 尽力冲刷未落盘的修改，不阻塞进程退出。
            if (_dirty && _cache is not null)
            {
                var json = JsonSerializer.Serialize(_cache, _json);
                File.WriteAllText(_storePath, json);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"退出时保存插件配置失败: {ex.Message}");
        }
        finally
        {
            _gate.Dispose();
        }
    }
}
