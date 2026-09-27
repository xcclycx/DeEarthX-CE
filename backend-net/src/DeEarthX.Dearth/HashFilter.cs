using System.Text.Json;
using System.Text.Json.Serialization;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;

namespace DeEarthX.Dearth;

public sealed class HashFilter : IBatchFilterStrategy
{
    private const string ModrinthBase = "https://api.modrinth.com";

    private readonly IDeEarthXHttpService _http;
    private readonly ILogService _log;

    public HashFilter(IDeEarthXHttpService http, ILogService log)
    {
        _http = http;
        _log = log;
    }

    public string Name => "hashes";

    public async Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0)
        {
            return result;
        }

        var hashToFilePath = new Dictionary<string, string>(files.Count, StringComparer.Ordinal);
        var hashes = new List<string>(files.Count);
        foreach (var f in files)
        {
            if (hashToFilePath.TryAdd(f.Hash, f.FilePath))
            {
                hashes.Add(f.Hash);
            }
        }

        try
        {
            var fileInfo = await _http.PostJsonAsync<Dictionary<string, HashResponseItem>>(
                $"{ModrinthBase}/v2/version_files",
                new { hashes, algorithm = "sha1" }).ConfigureAwait(false);

            if (fileInfo is null || fileInfo.Count == 0)
            {
                return result;
            }

            var projectIdToFilePath = new Dictionary<string, string>(StringComparer.Ordinal);
            var projectIds = new List<string>();
            foreach (var kv in fileInfo)
            {
                if (hashToFilePath.TryGetValue(kv.Key, out var filePath) && kv.Value.ProjectId is { Length: > 0 } pid)
                {
                    if (projectIdToFilePath.TryAdd(pid, filePath))
                    {
                        projectIds.Add(pid);
                    }
                }
            }

            if (projectIds.Count == 0)
            {
                return result;
            }

            var idsParam = Uri.EscapeDataString(JsonSerializer.Serialize(projectIds));
            var projects = await _http.GetJsonAsync<List<ModrinthProject>>(
                $"{ModrinthBase}/v2/projects?ids={idsParam}").ConfigureAwait(false);

            if (projects is null)
            {
                return result;
            }

            foreach (var p in projects)
            {
                if (p.ClientSide == "required" && p.ServerSide == "unsupported" &&
                    projectIdToFilePath.TryGetValue(p.Id, out var filePath))
                {
                    result.Add(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Hash 检查失败", ex);
        }

        return result;
    }

    private sealed class HashResponseItem
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = string.Empty;
    }

    public sealed class ModrinthProject
    {
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("client_side")]
        public string ClientSide { get; set; } = string.Empty;

        [JsonPropertyName("server_side")]
        public string ServerSide { get; set; } = string.Empty;
    }
}
