namespace DeEarthX.Dex.Optimization;

/// <summary>
/// 生成服务端 <c>server.properties</c>。仅在该文件不存在时写入，
/// 绝不覆盖整合包自带的配置。
/// </summary>
public static class ServerPropertiesFactory
{
    /// <summary>优化后的默认配置项，键为配置名，值为（值, 说明）。</summary>
    private static readonly (string Key, string Value, string Reason)[] OptimizedEntries =
    {
        ("view-distance", "8", "视距从默认 10 降到 8，显著降低服务端 CPU 与带宽占用"),
        ("simulation-distance", "6", "模拟距离从默认 10 降到 6，减少实体运算开销"),
        ("max-tick-time", "-1", "关闭看门狗超时，避免模组加载较慢时被强制终止"),
        ("sync-chunk-writes", "false", "关闭区块同步写入，减少 I/O 阻塞"),
        ("entity-broadcast-range-percentage", "75", "降低实体广播范围，减少网络包数量"),
        ("network-compression-threshold", "256", "网络压缩阈值保持 256，兼顾带宽与 CPU"),
        ("enable-jmx-monitoring", "false", "关闭 JMX 监控，省去一项常驻开销"),
        ("spawn-protection", "0", "关闭出生点保护，避免模组环境下误挡操作"),
        ("allow-flight", "true", "模组环境通常需要允许飞行，避免玩家被踢出"),
        ("enable-status", "true", "开放服务器列表状态查询"),
        ("level-name", "world", "世界目录名"),
        ("online-mode", "true", "保持正版验证；如需离线服请自行改为 false"),
        ("white-list", "false", "默认关闭白名单")
    };

    /// <summary>
    /// 构建 server.properties 文本，返回 (内容, 采用的优化项列表)。
    /// </summary>
    public static (string Content, List<ConfigChange> Changes) Build()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("#Minecraft server properties\n");
        sb.Append("#由 DeEarthX 生成，可按需修改\n");

        var changes = new List<ConfigChange>(OptimizedEntries.Length);
        foreach (var (key, value, reason) in OptimizedEntries)
        {
            sb.Append(key).Append('=').Append(value).Append('\n');
            changes.Add(new ConfigChange("server.properties", key, null, value, reason));
        }

        return (sb.ToString(), changes);
    }

    /// <summary>
    /// 若目标目录缺少 server.properties 则生成一份。返回生成的配置变更列表（未生成时为空）。
    /// </summary>
    public static async Task<List<ConfigChange>> EnsureAsync(string serverDir, bool dryRun, CancellationToken ct = default)
    {
        var path = Path.Combine(serverDir, "server.properties");
        if (File.Exists(path))
        {
            return new List<ConfigChange>();
        }

        var (content, changes) = Build();
        if (!dryRun)
        {
            await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
        }

        return changes;
    }
}

/// <summary>配置项变更记录，用于优化报告中的“修改前 → 修改后”对比表。</summary>
public sealed record ConfigChange(string File, string Key, string? Before, string After, string Reason);
