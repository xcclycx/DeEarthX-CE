using System.Text.Json;
using System.Text.Json.Serialization;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;

namespace DeEarthX.Dearth;

public sealed class ModrinthFilter : IBatchFilterStrategy
{
    private const string ApiBase = "https://api.modrinth.com/v2";
    private const int BatchSize = 100;

    private readonly IDeEarthXHttpService _http;
    private readonly ILogService _log;
    private readonly Dictionary<string, ModrinthProjectInfo> _cache = new(StringComparer.Ordinal);

    public ModrinthFilter(IDeEarthXHttpService http, ILogService log)
    {
        _http = http;
        _log = log;
    }

    public string Name => "modrinth";

    public async Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0)
        {
            return result;
        }

        var pairs = new List<(string FilePath, string ProjectId)>(files.Count);
        foreach (var file in files)
        {
            var pid = ExtractProjectId(file.Infos);
            if (!string.IsNullOrEmpty(pid))
            {
                pairs.Add((file.FilePath, pid));
            }
        }

        if (pairs.Count == 0)
        {
            return result;
        }

        var uniqueIds = pairs.Select(p => p.ProjectId).Distinct(StringComparer.Ordinal).ToList();
        var projectMap = await FetchProjectInfoAsync(uniqueIds).ConfigureAwait(false);

        foreach (var (filePath, pid) in pairs)
        {
            if (projectMap.TryGetValue(pid, out var info) && IsClientMod(info))
            {
                result.Add(filePath);
            }
        }

        return result;
    }

    private static string? ExtractProjectId(List<InfoFile> infos)
    {
        foreach (var info in infos)
        {
            if (!info.Name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase) &&
                !info.Name.Equals("modrinth.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(info.Data);
                if (doc.RootElement.TryGetProperty("project_id", out var pidEl) &&
                    pidEl.ValueKind == JsonValueKind.String)
                {
                    return pidEl.GetString();
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private async Task<Dictionary<string, ModrinthProjectInfo>> FetchProjectInfoAsync(List<string> projectIds)
    {
        var map = new Dictionary<string, ModrinthProjectInfo>(StringComparer.Ordinal);
        var uncached = new List<string>();
        foreach (var id in projectIds)
        {
            if (_cache.TryGetValue(id, out var cached))
            {
                map[id] = cached;
            }
            else
            {
                uncached.Add(id);
            }
        }

        if (uncached.Count == 0)
        {
            return map;
        }

        for (var i = 0; i < uncached.Count; i += BatchSize)
        {
            var batch = uncached.Skip(i).Take(BatchSize).ToList();
            var idsParam = Uri.EscapeDataString(string.Join(",", batch));
            try
            {
                var projects = await _http.GetJsonAsync<List<ModrinthProjectInfo>>(
                    $"{ApiBase}/projects?ids={idsParam}").ConfigureAwait(false);

                if (projects is null)
                {
                    continue;
                }

                foreach (var p in projects)
                {
                    if (!string.IsNullOrEmpty(p.Id))
                    {
                        map[p.Id] = p;
                        _cache[p.Id] = p;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("获取 Modrinth 项目信息失败", ex);
            }
        }

        return map;
    }

    private static bool IsClientMod(ModrinthProjectInfo project)
    {
        return project.ClientSide == "required" ||
               (project.ClientSide == "optional" && project.ServerSide == "unsupported");
    }

    public sealed class ModrinthProjectInfo
    {
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("client_side")]
        public string ClientSide { get; set; } = string.Empty;

        [JsonPropertyName("server_side")]
        public string ServerSide { get; set; } = string.Empty;

        [JsonPropertyName("project_type")]
        public string? ProjectType { get; set; }

        public List<string>? Categories { get; set; }
    }
}
