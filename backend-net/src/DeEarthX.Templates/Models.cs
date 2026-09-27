namespace DeEarthX.Templates;

public sealed class TemplateMetadata
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Created { get; set; }
    public string? Type { get; set; }
    public string? Minecraft { get; set; }
    public string? Loader { get; set; }
    public string? Icon { get; set; }
}

public sealed record Template(string Id, TemplateMetadata Metadata, string Path);

public sealed record TemplateInstallEvent(
    string Type,
    int? Percent,
    long? Downloaded,
    long? Total,
    string? Error,
    string? TemplateId);
