using System.Text.Json.Serialization;

namespace DeEarthX.Core.Models;

public record ModpackInfo(
    string Minecraft,
    string Loader,
    [property: JsonPropertyName("loader_version")] string LoaderVersion);
