using System.Text.Json.Serialization;

namespace DeEarthX.Dex.Optimization;

/// <summary>
/// 优化流水线产出的完整报告。前端会把它渲染成“优化报告”面板。
/// </summary>
public sealed class OptimizationReport
{
    /// <summary>是否为 dry-run（只分析不执行）。</summary>
    public bool DryRun { get; set; }

    /// <summary>是否整体成功。单项失败不会让整体失败，但会记录在 <see cref="Warnings"/>。</summary>
    public bool Success { get; set; } = true;

    /// <summary>服务端目录。</summary>
    public string ServerDirectory { get; set; } = string.Empty;

    /// <summary>本次分配的堆内存（MB）。</summary>
    public int MemoryMb { get; set; }

    /// <summary>是否使用了高内存（&gt;12GB）参数组。</summary>
    public bool HighMemoryProfile { get; set; }

    /// <summary>备份目录的绝对路径（未备份时为 null）。</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>已移除的客户端资源目录/文件（相对路径）。</summary>
    public List<string> RemovedClientResources { get; set; } = new();

    /// <summary>已清理的敏感信息文件（相对路径，不含具体内容）。</summary>
    public List<string> SanitizedSensitiveFiles { get; set; } = new();

    /// <summary>被移入 disabled_mods/ 的模组文件名。</summary>
    public List<string> DisabledMods { get; set; } = new();

    /// <summary>检测到的冗余项描述。</summary>
    public List<RedundancyFinding> Redundancies { get; set; } = new();

    /// <summary>生成的启动脚本文件名。</summary>
    public List<string> GeneratedScripts { get; set; } = new();

    /// <summary>生成的启动脚本中使用的 JVM 参数（便于前端展示）。</summary>
    public List<string> JvmArguments { get; set; } = new();

    /// <summary>Aikar's Flags 的实际取值（含高内存调整）。</summary>
    public List<AikarFlagsBuilder.FlagAdjustment> FlagAdjustments { get; set; } = new();

    /// <summary>server.properties 的配置项变更。</summary>
    public List<ConfigChange> ConfigChanges { get; set; } = new();

    /// <summary>需要用户手动处理的建议项。</summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>执行过程中的警告（非致命）。</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>各步骤耗时（毫秒）。</summary>
    public long ElapsedMs { get; set; }

    /// <summary>预估性能提升说明。</summary>
    public List<string> ExpectedGains { get; set; } = new();

    /// <summary>本次执行的操作日志（含被修改文件的路径与前后值）。</summary>
    public List<OptimizationLogEntry> Logs { get; set; } = new();

    /// <summary>统计：总处理文件数。</summary>
    [JsonIgnore]
    public int TotalAffectedFiles =>
        RemovedClientResources.Count + SanitizedSensitiveFiles.Count + DisabledMods.Count + Redundancies.Count;
}

/// <summary>冗余文件检测结果。</summary>
public sealed class RedundancyFinding
{
    /// <summary>类别：duplicate-mod / disabled-file / duplicate-library / orphan-config。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>相对路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>可读描述。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>该文件体积（字节）。</summary>
    public long SizeBytes { get; set; }

    /// <summary>是否已被自动处理。</summary>
    public bool Resolved { get; set; }
}

/// <summary>单条优化操作日志。</summary>
public sealed class OptimizationLogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>步骤名，例如 strip-client-resources。</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>动作，例如 move / compress / create / detect。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>受影响的文件路径（相对于服务端目录）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>补充说明（含修改前后的值）。</summary>
    public string? Detail { get; set; }
}
