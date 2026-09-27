using System.Text.Json;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;

namespace DeEarthX.Dearth;

public sealed class DexpubFilter : IBatchFilterStrategy
{
    private const string CheckUrl = "https://galaxy.xcclyc.com.cn/api/mod/check";

    private readonly IDeEarthXHttpService _http;
    private readonly ILogService _log;

    public DexpubFilter(IDeEarthXHttpService http, ILogService log)
    {
        _http = http;
        _log = log;
    }

    public string Name => "dexpub";

    public async Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files)
    {
        var (client, _) = await CheckAsync(files).ConfigureAwait(false);
        return client;
    }

    public async Task<(HashSet<string> Client, HashSet<string> Server)> CheckAsync(List<ModFileInfo> files)
    {
        var client = new HashSet<string>(StringComparer.Ordinal);
        var server = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0)
        {
            return (client, server);
        }

        var modIds = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var info in file.Infos)
            {
                try
                {
                    using var doc = JsonDocument.Parse(info.Data);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString()!;
                        if (id.Length > 0)
                        {
                            modIds.Add(id);
                            map[id] = file.FilePath;
                        }
                    }
                    else if (root.TryGetProperty("mods", out var modsEl) && modsEl.ValueKind == JsonValueKind.Array)
                    {
                        var first = modsEl.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object &&
                            first.TryGetProperty("modId", out var modIdEl) &&
                            modIdEl.ValueKind == JsonValueKind.String)
                        {
                            var modId = modIdEl.GetString()!;
                            if (modId.Length > 0)
                            {
                                modIds.Add(modId);
                                map[modId] = file.FilePath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"检查模组信息文件失败: {file.FileName}", ex);
                }
            }
        }

        if (modIds.Count == 0)
        {
            return (client, server);
        }

        try
        {
            var result = await _http.PostJsonAsync<Dictionary<string, bool>>(
                CheckUrl,
                new { modids = modIds }).ConfigureAwait(false);

            if (result is null)
            {
                return (client, server);
            }

            foreach (var kv in result)
            {
                if (!map.TryGetValue(kv.Key, out var filePath))
                {
                    continue;
                }

                if (kv.Value)
                {
                    client.Add(filePath);
                }
                else
                {
                    server.Add(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Dexpub 检查失败", ex);
        }

        return (client, server);
    }
}
