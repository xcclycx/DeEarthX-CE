using DeEarthX.Core.Abstractions;

namespace DeEarthX.Infrastructure;

public sealed class AppDirectoryProvider : IAppDirectoryProvider
{
    public const string DevEnvironmentFlag = "DEEARTHX_DEV";

    public string GetAppDirectory()
    {
        if (IsDevelopment())
        {
            return Directory.GetCurrentDirectory();
        }

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeEarthX");

        try
        {
            Directory.CreateDirectory(appDataDir);
        }
        catch
        {
            return Directory.GetCurrentDirectory();
        }

        return appDataDir;
    }

    private static bool IsDevelopment()
    {
        if (Environment.GetEnvironmentVariable(DevEnvironmentFlag) == "1")
        {
            return true;
        }

        var cwd = Directory.GetCurrentDirectory();
        var lower = cwd.ToLowerInvariant();
        if (lower.Contains("program files"))
        {
            return false;
        }

        return true;
    }
}
