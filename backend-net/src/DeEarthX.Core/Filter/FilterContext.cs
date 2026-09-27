namespace DeEarthX.Core.Filter;

public record FilterContext(
    string FilePath,
    string FileName,
    string ModId,
    string MinecraftVersion,
    string Loader,
    Dictionary<string, string>? Extra = null);
