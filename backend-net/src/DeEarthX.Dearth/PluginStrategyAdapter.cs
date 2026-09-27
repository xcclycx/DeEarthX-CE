using DeEarthX.Core.Filter;

namespace DeEarthX.Dearth;

public sealed class PluginStrategyAdapter : IBatchFilterStrategy
{
    private readonly IFilterStrategy _inner;
    private readonly FileExtractor _extractor;

    public PluginStrategyAdapter(IFilterStrategy inner, FileExtractor extractor)
    {
        _inner = inner;
        _extractor = extractor;
    }

    public string Name => _inner.Name;

    public async Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string modId = string.Empty;
            try
            {
                var meta = await _extractor.ExtractModMetaAsync(file, CancellationToken.None).ConfigureAwait(false);
                modId = meta?.ModId ?? string.Empty;
            }
            catch
            {
                modId = string.Empty;
            }

            var context = new FilterContext(
                file.FilePath,
                file.FileName,
                modId,
                string.Empty,
                string.Empty,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["hash"] = file.Hash });

            try
            {
                if (await _inner.ShouldFilterAsync(context).ConfigureAwait(false))
                {
                    result.Add(file.FilePath);
                }
            }
            catch
            {
            }
        }

        return result;
    }
}
