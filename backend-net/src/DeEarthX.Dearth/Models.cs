namespace DeEarthX.Dearth;

public record ModFileInfo(
    string FilePath,
    string FileName,
    string Hash,
    List<MixinFile> Mixins,
    List<InfoFile> Infos,
    byte[]? FileData = null);

public record MixinFile(string Name, string Data);

public record InfoFile(string Name, string Data);

public record ModMeta(
    string ModId,
    string? Version,
    string? Loader,
    string? IconUrl,
    string? Description,
    string? Author,
    List<string> MixinConfigs);

public enum ModSide
{
    Required,
    Optional,
    Unsupported,
    Unknown
}

public record ModCheckItem(
    string FileName,
    string FilePath,
    ModSide ClientSide,
    ModSide ServerSide,
    string Source,
    bool Checked,
    List<string>? Errors,
    List<SingleCheckResult>? AllResults,
    string? ModId,
    string? IconUrl,
    string? Description,
    string? Author);

public record SingleCheckResult(
    string Source,
    ModSide ClientSide,
    ModSide ServerSide,
    bool Checked,
    string? Error);

public record FilterResult(
    int FilteredCount,
    int MovedCount,
    int SkippedCount,
    int ErrorCount);

public interface IBatchFilterStrategy
{
    string Name { get; }

    Task<HashSet<string>> FilterBatchAsync(List<ModFileInfo> files);
}
