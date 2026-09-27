using System.Text.Json;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Dearth;

public sealed class MixinFilter : IBatchFilterStrategy
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogService _log;

    public MixinFilter(ILogService log)
    {
        _log = log;
    }

    public string Name => "mixins";

    public Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var isLib = file.FileName.Contains("lib", StringComparison.OrdinalIgnoreCase);
            if (isLib)
            {
                continue;
            }

            foreach (var mixin in file.Mixins)
            {
                try
                {
                    using var doc = JsonDocument.Parse(mixin.Data, JsonOptions);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var hasMixins = root.TryGetProperty("mixins", out var mixinsEl) &&
                                    mixinsEl.ValueKind == JsonValueKind.Array &&
                                    mixinsEl.GetArrayLength() > 0;
                    if (hasMixins)
                    {
                        continue;
                    }

                    var hasClient = root.TryGetProperty("client", out var clientEl) &&
                                    clientEl.ValueKind == JsonValueKind.Array &&
                                    clientEl.GetArrayLength() > 0;
                    if (hasClient)
                    {
                        result.Add(file.FilePath);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"Failed to parse mixin config: {file.FileName}/{mixin.Name}", ex);
                }
            }
        }

        return Task.FromResult(result);
    }
}
