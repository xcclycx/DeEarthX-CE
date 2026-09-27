using Tomlyn.Model;

namespace DeEarthX.Infrastructure.Toml;

public interface ITomlService
{
    TomlTable Parse(string content);

    TomlTable? ParseFile(string path);

    string GetModId(TomlTable table);
}

public sealed class TomlService : ITomlService
{
    public TomlTable Parse(string content)
    {
        return Tomlyn.TomlSerializer.Deserialize<TomlTable>(content)
               ?? throw new InvalidOperationException("Failed to parse TOML content");
    }

    public TomlTable? ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Tomlyn.TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public string GetModId(TomlTable table)
    {
        if (table.TryGetValue("mods", out var modsObj) && modsObj is TomlTableArray mods && mods.Count > 0)
        {
            var first = mods[0];
            if (first.TryGetValue("modId", out var idObj) && idObj is not null)
            {
                return idObj.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
