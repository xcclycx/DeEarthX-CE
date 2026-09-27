using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Process;

namespace DeEarthX.Infrastructure.Java;

public sealed record JavaVersion(int Major, int Minor, int Patch, string FullVersion, string Vendor);

public sealed record JavaCheckResult(bool Exists, JavaVersion? Version, string? Error);

public interface IJavaService
{
    Task<JavaCheckResult> CheckJavaAsync(string? javaPath = null, CancellationToken ct = default);

    Task<List<string>> DetectJavaPathsAsync(CancellationToken ct = default);
}

public sealed class JavaService : IJavaService
{
    private static readonly Regex VersionRegex = new("version \"(\\d+)(\\.(\\d+))?(\\.(\\d+))?", RegexOptions.Compiled);
    private static readonly Regex VendorRegex = new("(Java\\(TM\\)|OpenJDK).*Runtime Environment.*by (.*)", RegexOptions.Compiled);

    private readonly ILogService _log;
    private readonly IProcessService _processService;

    public JavaService(ILogService log, IProcessService processService)
    {
        _log = log;
        _processService = processService;
    }

    public async Task<JavaCheckResult> CheckJavaAsync(string? javaPath = null, CancellationToken ct = default)
    {
        var javaCmd = string.IsNullOrEmpty(javaPath) ? "java" : javaPath;
        try
        {
            var (exitCode, output) = await _processService.RunCaptureAsync($"{Quote(javaCmd)} -version", ct: ct).ConfigureAwait(false);
            if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
            {
                _log.Error("Java 检查失败", new InvalidOperationException($"exit code {exitCode}"));
                return new JavaCheckResult(false, null, "Java not found");
            }

            _log.Debug($"Java version output: {output}");

            var versionMatch = VersionRegex.Match(output);
            var vendorMatch = VendorRegex.Match(output);

            if (!versionMatch.Success)
            {
                return new JavaCheckResult(true, null, "解析 Java 版本失败");
            }

            var major = int.Parse(versionMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = versionMatch.Groups[3].Success ? int.Parse(versionMatch.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            var patch = versionMatch.Groups[5].Success ? int.Parse(versionMatch.Groups[5].Value, CultureInfo.InvariantCulture) : 0;
            var fullVersion = versionMatch.Value.Replace("version ", string.Empty);
            var vendor = vendorMatch.Success ? vendorMatch.Groups[2].Value.Trim() : "Unknown";

            var version = new JavaVersion(major, minor, patch, fullVersion, vendor);
            _log.Info($"检测到 Java: {JsonSerializer.Serialize(version)}");

            return new JavaCheckResult(true, version, null);
        }
        catch (Exception ex)
        {
            _log.Error("Java 检查异常", ex);
            return new JavaCheckResult(false, null, ex.Message);
        }
    }

    public async Task<List<string>> DetectJavaPathsAsync(CancellationToken ct = default)
    {
        var javaPaths = new List<string>();

        var basePaths = new[]
        {
            @"C:\Program Files\Java\",
            @"C:\Program Files (x86)\Java\",
            @"C:\Program Files\Eclipse Adoptium\",
            @"C:\Program Files\Eclipse Foundation\",
            @"C:\Program Files\Microsoft\",
            @"C:\Program Files\Amazon Corretto\",
            @"C:\Program Files\BellSoft\",
            @"C:\Program Files\Zulu\",
            @"C:\Program Files\Semeru\",
            @"C:\Program Files\Oracle\",
            @"C:\Program Files\RedHat\"
        };

        foreach (var basePath in basePaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(basePath))
                {
                    continue;
                }

                foreach (var version in Directory.EnumerateDirectories(basePath))
                {
                    var javaExe = Path.Combine(version, "bin", "java.exe");
                    if (File.Exists(javaExe))
                    {
                        javaPaths.Add(javaExe);
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            var (exitCode, pathOutput) = await _processService.RunCaptureAsync("where java", ct: ct).ConfigureAwait(false);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(pathOutput))
            {
                foreach (var line in pathOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !javaPaths.Contains(trimmed))
                    {
                        javaPaths.Add(trimmed);
                    }
                }
            }
        }
        catch
        {
        }

        return javaPaths.Distinct().ToList();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        if (value.Contains(' ') && !value.StartsWith('"'))
        {
            return "\"" + value + "\"";
        }
        return value;
    }
}
