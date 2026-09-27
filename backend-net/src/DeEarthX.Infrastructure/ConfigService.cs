using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Configuration;

namespace DeEarthX.Infrastructure;

public sealed class ConfigService : IConfigService
{
    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly ILogService _log;
    private readonly object _lock = new();
    private DeEarthXConfig? _cache;

    public ConfigService(IAppDirectoryProvider appDirectoryProvider, ILogService log)
    {
        _appDirectoryProvider = appDirectoryProvider;
        _log = log;
    }

    private string ConfigPath => Path.Combine(_appDirectoryProvider.GetAppDirectory(), "config.json");

    public DeEarthXConfig Get()
    {
        lock (_lock)
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var path = ConfigPath;
            DeEarthXConfig config;
            if (!File.Exists(path))
            {
                config = DeEarthXConfig.CreateDefault();
                try
                {
                    WriteInternal(config);
                }
                catch (Exception ex)
                {
                    _log.Error("写入默认配置文件失败", ex);
                }
            }
            else
            {
                try
                {
                    var json = File.ReadAllText(path);
                    config = JsonSerializer.Deserialize<DeEarthXConfig>(json, DeEarthXJsonOptions.Default)
                             ?? DeEarthXConfig.CreateDefault();
                }
                catch (Exception ex)
                {
                    _log.Error("读取配置文件失败，使用默认配置", ex);
                    config = DeEarthXConfig.CreateDefault();
                }
            }

            config = ApplyEnvironmentOverrides(config);
            _cache = config;
            _log.Debug("Loaded config", config);
            return config;
        }
    }

    public void Write(DeEarthXConfig config)
    {
        lock (_lock)
        {
            try
            {
                WriteInternal(config);
                _cache = config;
                _log.Info("Config file written successfully");
            }
            catch (Exception ex)
            {
                _log.Error("写入配置文件失败", ex);
            }
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }

    private void WriteInternal(DeEarthXConfig config)
    {
        var path = ConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, BuildWriteOptions());
        File.WriteAllText(path, json);
    }

    private static JsonSerializerOptions BuildWriteOptions()
    {
        var opts = new JsonSerializerOptions(DeEarthXJsonOptions.Default) { WriteIndented = true };
        return opts;
    }

    private static DeEarthXConfig ApplyEnvironmentOverrides(DeEarthXConfig c)
    {
        var mirror = c.Mirror;
        var filter = c.Filter;

        var result = new DeEarthXConfig
        {
            Mirror = new MirrorConfig
            {
                Bmclapi = GetEnvBool("DEEARTHX_MIRROR_BMCLAPI", mirror.Bmclapi),
                Mcimirror = GetEnvBool("DEEARTHX_MIRROR_MCIMIRROR", mirror.Mcimirror),
                McimirrorModrinthOnly = GetEnvBoolNullable("DEEARTHX_MIRROR_MCIMIRROR_MODRINTH_ONLY", mirror.McimirrorModrinthOnly)
            },
            Filter = new FilterConfig
            {
                Hashes = GetEnvBool("DEEARTHX_FILTER_HASHES", filter.Hashes),
                Dexpub = GetEnvBool("DEEARTHX_FILTER_DEXPUB", filter.Dexpub),
                Mixins = GetEnvBool("DEEARTHX_FILTER_MIXINS", filter.Mixins),
                Modrinth = GetEnvBool("DEEARTHX_FILTER_MODRINTH", filter.Modrinth),
                McmodFilter = GetEnvBoolNullable("DEEARTHX_FILTER_MCMOD", filter.McmodFilter ?? false)
            },
            Oaf = GetEnvBool("DEEARTHX_OAF", c.Oaf),
            AutoZip = GetEnvBool("DEEARTHX_AUTO_ZIP", c.AutoZip),
            Port = GetEnvInt("DEEARTHX_PORT", c.Port ?? DeEarthXConfig.CreateDefault().Port!.Value),
            Host = GetEnvString("DEEARTHX_HOST", c.Host ?? DeEarthXConfig.CreateDefault().Host!),
            JavaPath = GetEnvStringNullable("DEEARTHX_JAVA_PATH", c.JavaPath),
            // 优化与日志配置没有环境变量入口，原样保留，避免被默认值覆盖。
            Optimization = c.Optimization ?? OptimizationConfig.CreateDefault(),
            Log = c.Log ?? LogConfig.CreateDefault()
        };

        return result;
    }

    private static string? GetRawEnv(string key) => Environment.GetEnvironmentVariable(key);

    private static bool GetEnvBool(string key, bool defaultValue)
    {
        var raw = GetRawEnv(key);
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw == "1"
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? GetEnvBoolNullable(string key, bool? defaultValue)
    {
        var raw = GetRawEnv(key);
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw == "1"
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetEnvInt(string key, int defaultValue)
    {
        var raw = GetRawEnv(key);
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }
        return int.TryParse(raw, out var v) ? v : defaultValue;
    }

    private static int? GetEnvIntNullable(string key, int? defaultValue)
    {
        var raw = GetRawEnv(key);
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }
        return int.TryParse(raw, out var v) ? v : defaultValue;
    }

    private static string GetEnvString(string key, string defaultValue)
    {
        var raw = GetRawEnv(key);
        return string.IsNullOrEmpty(raw) ? defaultValue : raw;
    }

    private static string? GetEnvStringNullable(string key, string? defaultValue)
    {
        var raw = GetRawEnv(key);
        return string.IsNullOrEmpty(raw) ? defaultValue : raw;
    }
}
