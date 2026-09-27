using DeEarthX.Core.Configuration;

namespace DeEarthX.Infrastructure.Downloads;

public sealed record MirrorUrls(
    string ModrinthUrl,
    string CurseforgeUrl,
    string ModrinthDurl,
    string CurseforgeDurl);

public sealed record DownloadItem(
    string Url,
    string FilePath,
    string? ExpectedHash = null);

public sealed record DownloadProgress(
    int Index,
    int Total,
    string Name);

public static class MirrorResolver
{
    public static MirrorUrls Get(DeEarthXConfig config)
    {
        var mirror = config.Mirror;
        if (mirror.Mcimirror)
        {
            return new MirrorUrls(
                "https://mod.mcimirror.top/modrinth",
                "https://mod.mcimirror.top/curseforge",
                "https://mod.mcimirror.top",
                "https://mod.mcimirror.top");
        }

        if (mirror.McimirrorModrinthOnly is true)
        {
            return new MirrorUrls(
                "https://mod.mcimirror.top/modrinth",
                "https://api.curseforge.com",
                "https://mod.mcimirror.top",
                "https://edge.forgecdn.net");
        }

        return new MirrorUrls(
            "https://api.modrinth.com",
            "https://api.curseforge.com",
            "https://cdn.modrinth.com",
            "https://edge.forgecdn.net");
    }

    public static bool IsMcMirrorUrl(string url)
    {
        return url.Contains("mod.mcimirror.top", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] CurseforgeCdnHosts =
    {
        "https://edge.forgecdn.net",
        "https://mediafilez.forgecdn.net",
        "http://edge.forgecdn.net",
        "http://mediafilez.forgecdn.net"
    };

    private static readonly string[] ModrinthCdnHosts =
    {
        "https://cdn.modrinth.com",
        "http://cdn.modrinth.com"
    };

    public static string ResolveCurseforgeCdnUrl(string url, DeEarthXConfig config)
    {
        var mirror = MirrorResolver.Get(config);
        if (string.Equals(mirror.CurseforgeDurl, "https://edge.forgecdn.net", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        foreach (var host in CurseforgeCdnHosts)
        {
            if (url.StartsWith(host, StringComparison.OrdinalIgnoreCase))
            {
                return host.Length <= url.Length
                    ? mirror.CurseforgeDurl + url[host.Length..]
                    : url;
            }
        }

        return url;
    }

    public static string ResolveModrinthCdnUrl(string url, DeEarthXConfig config)
    {
        var mirror = MirrorResolver.Get(config);
        if (string.Equals(mirror.ModrinthDurl, "https://cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        foreach (var host in ModrinthCdnHosts)
        {
            if (url.StartsWith(host, StringComparison.OrdinalIgnoreCase))
            {
                return host.Length <= url.Length
                    ? mirror.ModrinthDurl + url[host.Length..]
                    : url;
            }
        }

        return url;
    }

    public static string ResolveMavenUrl(string url, DeEarthXConfig config)
    {
        if (!config.Mirror.Mcimirror)
        {
            return url;
        }

        if (url.Contains("maven.neoforged.net", StringComparison.OrdinalIgnoreCase))
        {
            return url.Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase);
        }

        if (url.Contains("maven.minecraftforge.net", StringComparison.OrdinalIgnoreCase))
        {
            return url.Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase);
        }

        return url;
    }
}
