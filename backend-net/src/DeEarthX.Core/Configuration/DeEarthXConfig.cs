namespace DeEarthX.Core.Configuration;

public class DeEarthXConfig
{
    public MirrorConfig Mirror { get; set; } = new();
    public FilterConfig Filter { get; set; } = new();
    public bool Oaf { get; set; }
    public bool AutoZip { get; set; }
    public int? Port { get; set; }
    public string? Host { get; set; }
    public string? JavaPath { get; set; }

    /// <summary>
    /// 导出优化流水线配置（Aikar's Flags、启动脚本、资源剥离、报告等）。
    /// 缺失时使用默认值，保证旧配置文件仍可正常读取。
    /// </summary>
    public OptimizationConfig Optimization { get; set; } = new();

    /// <summary>日志留存策略。</summary>
    public LogConfig Log { get; set; } = new();

    public static DeEarthXConfig CreateDefault() => new()
    {
        Mirror = new MirrorConfig
        {
            Bmclapi = true,
            Mcimirror = true,
            McimirrorModrinthOnly = false
        },
        Filter = new FilterConfig
        {
            Hashes = true,
            Dexpub = true,
            Mixins = true,
            Modrinth = false,
            McmodFilter = false
        },
        Oaf = true,
        AutoZip = false,
        Port = 37019,
        Host = "localhost",
        JavaPath = null,
        Optimization = OptimizationConfig.CreateDefault(),
        Log = LogConfig.CreateDefault()
    };
}

public class MirrorConfig
{
    public bool Bmclapi { get; set; }
    public bool Mcimirror { get; set; }
    public bool? McimirrorModrinthOnly { get; set; }
}

public class FilterConfig
{
    public bool Hashes { get; set; }
    public bool Dexpub { get; set; }
    public bool Mixins { get; set; }
    public bool Modrinth { get; set; }
    public bool? McmodFilter { get; set; }
}

/// <summary>
/// 服务端导出优化配置。
/// </summary>
public class OptimizationConfig
{
    /// <summary>优化流水线总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>生成启动脚本（start.bat / start.sh）。</summary>
    public bool GenerateLaunchScript { get; set; } = true;

    /// <summary>在启动脚本中注入 Aikar's Flags。</summary>
    public bool AikarFlags { get; set; } = true;

    /// <summary>剥离客户端专用资源目录（resourcepacks/shaderpacks/saves 等）。</summary>
    public bool StripClientResources { get; set; } = true;

    /// <summary>清理敏感信息文件（options.txt、servers.dat、账号 Token 等）。</summary>
    public bool SanitizeSensitive { get; set; } = true;

    /// <summary>检测冗余文件（重复模组、.disabled 残留等）。</summary>
    public bool DetectRedundant { get; set; } = true;

    /// <summary>缺少 server.properties 时生成一份带优化项的配置。</summary>
    public bool GenerateServerProperties { get; set; } = true;

    /// <summary>
    /// 分配给服务端的内存（MB）。为 0 或负数时自动探测（系统内存的一半，限制在 2GB~12GB）。
    /// </summary>
    public int MemoryMb { get; set; }

    /// <summary>执行前把将被改动的文件备份到 backup/ 目录。强烈建议保持开启。</summary>
    public bool Backup { get; set; } = true;

    /// <summary>dry-run：只分析并生成报告，不修改任何文件。</summary>
    public bool DryRun { get; set; }

    public static OptimizationConfig CreateDefault() => new()
    {
        Enabled = true,
        GenerateLaunchScript = true,
        AikarFlags = true,
        StripClientResources = true,
        SanitizeSensitive = true,
        DetectRedundant = true,
        GenerateServerProperties = true,
        MemoryMb = 0,
        Backup = true,
        DryRun = false
    };
}

/// <summary>
/// 日志留存配置。
/// </summary>
public class LogConfig
{
    /// <summary>日志文件保留天数，超期文件在清理时删除。</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>单个日志文件的最大体积（MB），超出后轮转。</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    public static LogConfig CreateDefault() => new()
    {
        RetentionDays = 14,
        MaxFileSizeMb = 10
    };
}
