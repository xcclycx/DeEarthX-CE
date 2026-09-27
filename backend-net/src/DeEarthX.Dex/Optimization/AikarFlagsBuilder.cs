namespace DeEarthX.Dex.Optimization;

/// <summary>
/// 生成 Aikar's Flags（针对 Minecraft 内存分配模式优化的 G1 垃圾回收器参数）。
///
/// 关键原则：<c>-Xms</c> 必须等于 <c>-Xmx</c>。Xms 低于 Xmx 意味着存在未使用的内存，
/// 而 G1 在更多内存下表现更好，它会自适应地调整区域大小以优化暂停时间。
///
/// 当分配内存超过 12GB 时，Aikar's Flags 需要调整五个参数（见 <see cref="HighMemoryThresholdMb"/>）。
/// 参考：https://mcflags.emc.gs
/// </summary>
public static class AikarFlagsBuilder
{
    /// <summary>超过此内存（MB）时需要切换到高内存参数组。12GB。</summary>
    public const int HighMemoryThresholdMb = 12 * 1024;

    /// <summary>自动探测内存时的下限（MB）。</summary>
    public const int MinAutoMemoryMb = 2 * 1024;

    /// <summary>自动探测内存时的上限（MB）。没有 Aikar 官方推荐值，这里保守限制在 12GB。</summary>
    public const int MaxAutoMemoryMb = 12 * 1024;

    /// <summary>
    /// 把内存值规整到安全区间：低于 512MB 会被抬高，超过 32768MB 会被压低。
    /// </summary>
    public static int NormalizeMemoryMb(int memoryMb)
    {
        if (memoryMb < 512) return 512;
        if (memoryMb > 32768) return 32768;
        return memoryMb;
    }

    /// <summary>
    /// 根据系统内存自动探测合适的内存分配值：系统内存的一半，限制在 2GB~12GB。
    /// 探测失败时回退到 4GB。
    /// </summary>
    public static int DetectAutoMemoryMb()
    {
        try
        {
            var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalBytes <= 0)
            {
                return 4096;
            }

            var halfMb = (int)(totalBytes / 1024 / 1024 / 2);
            if (halfMb < MinAutoMemoryMb) return MinAutoMemoryMb;
            if (halfMb > MaxAutoMemoryMb) return MaxAutoMemoryMb;
            return halfMb;
        }
        catch
        {
            return 4096;
        }
    }

    /// <summary>
    /// 解析配置中的内存设置：0 或负数表示自动探测。
    /// </summary>
    public static int ResolveMemoryMb(int configuredMemoryMb)
        => configuredMemoryMb > 0 ? NormalizeMemoryMb(configuredMemoryMb) : DetectAutoMemoryMb();

    /// <summary>
    /// 是否使用高内存参数组（&gt;12GB）。
    /// </summary>
    public static bool IsHighMemory(int memoryMb) => memoryMb > HighMemoryThresholdMb;

    /// <summary>
    /// 构建完整的 JVM 参数列表（不含 java 可执行文件本身，不含 -jar）。
    /// </summary>
    public static List<string> BuildArguments(int memoryMb, bool includeAikarFlags = true)
    {
        var ram = NormalizeMemoryMb(memoryMb);
        var high = IsHighMemory(ram);

        var args = new List<string>
        {
            // -Xms 必须等于 -Xmx。
            $"-Xms{ram}M",
            $"-Xmx{ram}M"
        };

        if (includeAikarFlags)
        {
            args.AddRange(BuildAikarFlags(high));
        }

        return args;
    }

    /// <summary>
    /// Aikar's Flags 本体。超过 12GB 时替换其中五个参数。
    /// </summary>
    public static List<string> BuildAikarFlags(bool highMemory)
    {
        // 大于 12GB 时调整的五个参数。
        var g1NewSizePercent = highMemory ? 40 : 30;
        var g1MaxNewSizePercent = highMemory ? 50 : 40;
        var g1HeapRegionSize = highMemory ? "16M" : "8M";
        var g1ReservePercent = highMemory ? 15 : 20;
        var initiatingHeapOccupancyPercent = highMemory ? 20 : 15;

        return new List<string>
        {
            "-XX:+UseG1GC",
            "-XX:+ParallelRefProcEnabled",
            "-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+DisableExplicitGC",
            "-XX:+AlwaysPreTouch",
            $"-XX:G1NewSizePercent={g1NewSizePercent}",
            $"-XX:G1MaxNewSizePercent={g1MaxNewSizePercent}",
            $"-XX:G1HeapRegionSize={g1HeapRegionSize}",
            $"-XX:G1ReservePercent={g1ReservePercent}",
            "-XX:G1HeapWastePercent=5",
            "-XX:G1MixedGCCountTarget=4",
            $"-XX:InitiatingHeapOccupancyPercent={initiatingHeapOccupancyPercent}",
            "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5",
            "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem",
            "-XX:MaxTenuringThreshold=1",
            "-Dusing.aikars.flags=https://mcflags.emc.gs",
            "-Daikars.new.flags=true"
        };
    }

    /// <summary>
    /// 单行形式的参数，便于写入 .bat。
    /// </summary>
    public static string BuildArgumentLine(int memoryMb, bool includeAikarFlags = true)
        => string.Join(' ', BuildArguments(memoryMb, includeAikarFlags));

    /// <summary>
    /// 供 UI 展示的参数说明表（调整前 → 调整后）。
    /// </summary>
    public static List<FlagAdjustment> DescribeAdjustments(int memoryMb)
    {
        var ram = NormalizeMemoryMb(memoryMb);
        var high = IsHighMemory(ram);

        return new List<FlagAdjustment>
        {
            new("-Xms / -Xmx", null, $"{ram}M", "Xms 必须等于 Xmx，避免存在未使用的堆内存"),
            new("-XX:G1NewSizePercent", "30", high ? "40" : "30", high ? "高内存下扩大新生代下限" : "默认 Aikar 值"),
            new("-XX:G1MaxNewSizePercent", "40", high ? "50" : "40", high ? "高内存下扩大新生代上限" : "默认 Aikar 值"),
            new("-XX:G1HeapRegionSize", "8M", high ? "16M" : "8M", high ? "大堆使用更大的堆区域" : "默认 Aikar 值"),
            new("-XX:G1ReservePercent", "20", high ? "15" : "20", high ? "大堆降低保留区比例" : "默认 Aikar 值"),
            new("-XX:InitiatingHeapOccupancyPercent", "15", high ? "20" : "15", high ? "大堆延后并发周期启动" : "默认 Aikar 值")
        };
    }

    /// <summary>参数调整记录，用于优化报告中的“修改前 → 修改后”对比表。</summary>
    public sealed record FlagAdjustment(string Flag, string? Before, string After, string Reason);
}
