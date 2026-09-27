using System.Collections.Concurrent;
using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Infrastructure.Http;

namespace DeEarthX.Web;

public sealed class DownloadVersionService
{
    private readonly IDeEarthXHttpService _http;
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public DownloadVersionService(IDeEarthXHttpService http)
    {
        _http = http;
    }

    /// <summary>
    /// 安全读取一个「对象」属性。
    ///
    /// 上游（BMCLAPI 等）偶尔会返回 <c>"build": null</c> 这类数据：属性确实存在，
    /// 但值是 Null。对 Null 的 JsonElement 调用 TryGetProperty 会抛
    /// InvalidOperationException（"requires an element of type 'Object', but the
    /// target element has type 'Null'"，见 GetForgePromosAsync），最终以 500 暴露给前端。
    /// 这里统一先判类型再下钻。
    /// </summary>
    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    /// <summary>安全读取「数组」属性。</summary>
    private static bool TryGetArray(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// 安全读取字符串属性：非字符串（null / 数字 / 对象）一律返回 null，
    /// 避免 JsonElement.GetString() 抛 InvalidOperationException。
    /// </summary>
    private static string? GetStringOrNull(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>安全读取布尔属性：非布尔一律按 false 处理。</summary>
    private static bool GetBoolOrDefault(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return false;
        if (!parent.TryGetProperty(name, out var value)) return false;
        return value.ValueKind == JsonValueKind.True;
    }

    public async Task<object> GetMinecraftVersionsAsync(CancellationToken ct = default)
    {
        const string key = "minecraft-versions";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";
        var data = await _http.GetJsonAsync<JsonElement>(url, ct);
        var versions = new List<object>();
        if (TryGetArray(data, "versions", out var vers))
        {
            foreach (var v in vers.EnumerateArray())
            {
                var id = GetStringOrNull(v, "id");
                var type = GetStringOrNull(v, "type");
                if (id is not null)
                {
                    versions.Add(new { id, type });
                }
            }
        }
        var result = new { versions };
        _cache[key] = result;
        return result;
    }

    public async Task<object> GetForgePromosAsync(CancellationToken ct = default)
    {
        const string key = "forge-promos";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = "https://bmclapi2.bangbang93.com/forge/promos";
        var data = await _http.GetJsonAsync<JsonElement>(url, ct);
        var promos = new Dictionary<string, Dictionary<string, string?>>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in data.EnumerateArray())
            {
                // 部分条目形如 {"name":"xxx-latest","build":null}，必须先判类型再下钻，
                // 否则 TryGetProperty 会直接抛 InvalidOperationException。
                if (!TryGetObject(entry, "build", out var build)) continue;

                var mcversion = GetStringOrNull(build, "mcversion");
                var version = GetStringOrNull(build, "version");
                if (mcversion is null || version is null) continue;

                if (!promos.ContainsKey(mcversion)) promos[mcversion] = new Dictionary<string, string?>();

                var name = GetStringOrNull(entry, "name");
                if (name is not null && name.EndsWith("-latest")) promos[mcversion]["latest"] = version;
                else if (name is not null && name.EndsWith("-recommended")) promos[mcversion]["recommended"] = version;
            }
        }
        _cache[key] = promos;
        return promos;
    }

    public async Task<object> GetForgeVersionsAsync(string mcver, CancellationToken ct = default)
    {
        var key = $"forge-versions:{mcver}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = $"https://bmclapi2.bangbang93.com/forge/minecraft/{mcver}";
        var data = await _http.GetJsonAsync<JsonElement>(url, ct);
        var versions = new List<object>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in data.EnumerateArray())
            {
                var version = GetStringOrNull(v, "version");
                var mcversion = GetStringOrNull(v, "mcversion");
                string? hash = null;
                if (TryGetArray(v, "files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var category = GetStringOrNull(f, "category");
                        var format = GetStringOrNull(f, "format");
                        if (category == "installer" && format == "jar")
                        {
                            hash = GetStringOrNull(f, "hash");
                            break;
                        }
                    }
                }
                if (version is not null && mcversion is not null)
                {
                    versions.Add(new { version, mcversion, hash });
                }
            }
        }
        _cache[key] = versions;
        return versions;
    }

    public async Task<object> GetNeoForgeVersionsAsync(string mcver, CancellationToken ct = default)
    {
        var key = $"neoforge-versions:{mcver}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = $"https://bmclapi2.bangbang93.com/neoforge/list/{mcver}";
        var data = await _http.GetJsonAsync<JsonElement>(url, ct);
        var versions = new List<object>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            var arr = data.EnumerateArray().ToList();
            for (var i = 0; i < arr.Count; i++)
            {
                var v = arr[i];
                var version = GetStringOrNull(v, "version");
                var mcversion = GetStringOrNull(v, "mcversion");
                var installerPath = GetStringOrNull(v, "installerPath");
                var latest = i == arr.Count - 1;
                if (version is not null && mcversion is not null)
                {
                    versions.Add(new { version, mcversion, installerPath, latest });
                }
            }
        }
        _cache[key] = versions;
        return versions;
    }

    public async Task<object> GetFabricVersionsAsync(string mcver, CancellationToken ct = default)
    {
        var key = $"fabric-versions:{mcver}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = $"https://meta.fabricmc.net/v1/versions/loader/{mcver}";
        var data = await _http.GetJsonAsync<JsonElement>(url, ct);
        var versions = new List<dynamic>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in data.EnumerateArray())
            {
                if (TryGetObject(v, "loader", out var loader))
                {
                    var version = GetStringOrNull(loader, "version");
                    var stable = GetBoolOrDefault(loader, "stable");
                    if (version is not null)
                    {
                        versions.Add(new { version, stable });
                    }
                }
            }
        }
        var sorted = versions.OrderByDescending(x => x.stable ? 1 : 0).ToList();
        _cache[key] = sorted;
        return sorted;
    }
}
