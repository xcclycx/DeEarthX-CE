using System.Text;

namespace DeEarthX.Dex.Optimization;

/// <summary>
/// 根据目标操作系统生成服务端启动脚本：
/// <list type="bullet">
///   <item>Windows：<c>start.bat</c>，包含 Java 路径探测（优先 Java 21+）、内存参数、Aikar's Flags，以及 <c>PAUSE</c> 防止崩溃后窗口关闭。</item>
///   <item>Linux/macOS：<c>start.sh</c>，包含 <c>set -euo pipefail</c>、目录自动切换与可执行权限设置。</item>
/// </list>
/// </summary>
public static class LaunchScriptGenerator
{
    /// <summary>脚本查找服务端 jar 时的候选名（按优先级）。</summary>
    private static readonly string[] JarCandidates =
    {
        "server.jar",
        "fabric-server-launch.jar",
        "fabric-server-launch.jar.disabled",
        "quilt-server-launch.jar",
        "forge-*.jar",
        "neoforge-*.jar",
        "paper-*.jar",
        "spigot-*.jar",
        "purpur-*.jar"
    };

    /// <summary>
    /// 在服务端目录中探测真正的启动 jar 文件名。找不到时回退到 <c>server.jar</c>。
    /// </summary>
    public static string DetectServerJar(string serverDir)
    {
        // 1) 精确匹配常见启动器文件名。
        foreach (var name in new[]
                 {
                     "fabric-server-launch.jar",
                     "quilt-server-launch.jar",
                     "server.jar"
                 })
        {
            if (File.Exists(Path.Combine(serverDir, name)))
            {
                return name;
            }
        }

        // 2) 通配匹配（forge/neoforge/paper 等带版本号的 jar）。
        foreach (var pattern in new[] { "forge-*.jar", "neoforge-*.jar", "paper-*.jar", "purpur-*.jar", "spigot-*.jar" })
        {
            try
            {
                var match = Directory
                    .EnumerateFiles(serverDir, pattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (match is not null)
                {
                    return Path.GetFileName(match);
                }
            }
            catch
            {
                // 忽略访问异常，继续尝试下一个模式。
            }
        }

        return "server.jar";
    }

    /// <summary>
    /// 生成 Windows 启动脚本内容（CRLF 行尾）。
    /// </summary>
    public static string BuildWindowsScript(string serverJar, int memoryMb, bool includeAikarFlags)
    {
        var argumentLine = AikarFlagsBuilder.BuildArgumentLine(memoryMb, includeAikarFlags);

        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append("chcp 65001 >nul\r\n");
        sb.Append("setlocal enabledelayedexpansion\r\n");
        sb.Append("title DeEarthX Minecraft Server\r\n");
        sb.Append("cd /d \"%~dp0\"\r\n");
        sb.Append("\r\n");
        sb.Append("set \"SERVER_JAR=").Append(serverJar).Append("\"\r\n");
        sb.Append("\r\n");
        sb.Append("rem ---- 定位 Java：优先 Java 21+，其次 JAVA_HOME，最后 PATH ----\r\n");
        sb.Append("set \"JAVA_EXE=\"\r\n");
        sb.Append("\r\n");
        sb.Append("rem 1) 常见 JDK 21/22/23 安装目录，按版本从高到低探测\r\n");
        sb.Append("for %%V in (23 22 21 17) do (\r\n");
        sb.Append("    if not defined JAVA_EXE (\r\n");
        sb.Append("        for /d %%D in (\"%ProgramFiles%\\Java\\jdk-%%V*\") do (\r\n");
        sb.Append("            if exist \"%%~fD\\bin\\java.exe\" set \"JAVA_EXE=%%~fD\\bin\\java.exe\"\r\n");
        sb.Append("        )\r\n");
        sb.Append("    )\r\n");
        sb.Append(")\r\n");
        sb.Append("for /d %%D in (\"%ProgramFiles%\\Eclipse Adoptium\\jdk-21*\") do (\r\n");
        sb.Append("    if not defined JAVA_EXE if exist \"%%~fD\\bin\\java.exe\" set \"JAVA_EXE=%%~fD\\bin\\java.exe\"\r\n");
        sb.Append(")\r\n");
        sb.Append("for /d %%D in (\"%ProgramFiles%\\Microsoft\\jdk-21*\") do (\r\n");
        sb.Append("    if not defined JAVA_EXE if exist \"%%~fD\\bin\\java.exe\" set \"JAVA_EXE=%%~fD\\bin\\java.exe\"\r\n");
        sb.Append(")\r\n");
        sb.Append("\r\n");
        sb.Append("rem 2) JAVA_HOME\r\n");
        sb.Append("if not defined JAVA_EXE if defined JAVA_HOME if exist \"%JAVA_HOME%\\bin\\java.exe\" set \"JAVA_EXE=%JAVA_HOME%\\bin\\java.exe\"\r\n");
        sb.Append("\r\n");
        sb.Append("rem 3) PATH\r\n");
        sb.Append("if not defined JAVA_EXE for %%J in (java.exe) do set \"JAVA_EXE=%%~$PATH:J\"\r\n");
        sb.Append("\r\n");
        sb.Append("if not defined JAVA_EXE (\r\n");
        sb.Append("    echo [错误] 未找到 Java。请安装 Java 21 或更高版本，或设置 JAVA_HOME 环境变量。\r\n");
        sb.Append("    echo         下载地址: https://adoptium.net/\r\n");
        sb.Append("    pause\r\n");
        sb.Append("    exit /b 1\r\n");
        sb.Append(")\r\n");
        sb.Append("\r\n");
        sb.Append("echo 使用的 Java: %JAVA_EXE%\r\n");
        sb.Append("\"%JAVA_EXE%\" -version\r\n");
        sb.Append("echo.\r\n");
        sb.Append("echo 正在启动服务端 %SERVER_JAR% ...\r\n");
        sb.Append("\r\n");
        sb.Append("\"%JAVA_EXE%\" ").Append(argumentLine).Append(" -jar \"%SERVER_JAR%\" nogui\r\n");
        sb.Append("\r\n");
        sb.Append("echo.\r\n");
        sb.Append("echo 服务端已退出，退出码: %ERRORLEVEL%\r\n");
        sb.Append("pause\r\n");
        sb.Append("endlocal\r\n");

        return sb.ToString();
    }

    /// <summary>
    /// 生成 Linux/macOS 启动脚本内容（LF 行尾）。
    /// </summary>
    public static string BuildUnixScript(string serverJar, int memoryMb, bool includeAikarFlags)
    {
        var argumentLine = AikarFlagsBuilder.BuildArgumentLine(memoryMb, includeAikarFlags);

        var sb = new StringBuilder();
        sb.Append("#!/usr/bin/env bash\n");
        sb.Append("# DeEarthX 自动生成的服务端启动脚本\n");
        sb.Append("# 关键原则：-Xms 必须等于 -Xmx。\n");
        sb.Append("set -euo pipefail\n");
        sb.Append("\n");
        sb.Append("# 切换到脚本所在目录，保证相对路径正确\n");
        sb.Append("cd \"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd)\"\n");
        sb.Append("\n");
        sb.Append("SERVER_JAR=\"").Append(serverJar).Append("\"\n");
        sb.Append("\n");
        sb.Append("# ---- 定位 Java：优先 JAVA_HOME，其次 PATH ----\n");
        sb.Append("if [[ -n \"${JAVA_HOME:-}\" && -x \"${JAVA_HOME}/bin/java\" ]]; then\n");
        sb.Append("    JAVA_EXE=\"${JAVA_HOME}/bin/java\"\n");
        sb.Append("elif command -v java >/dev/null 2>&1; then\n");
        sb.Append("    JAVA_EXE=\"$(command -v java)\"\n");
        sb.Append("else\n");
        sb.Append("    echo \"[错误] 未找到 Java。请安装 Java 21 或更高版本，或设置 JAVA_HOME 环境变量。\" >&2\n");
        sb.Append("    echo \"        下载地址: https://adoptium.net/\" >&2\n");
        sb.Append("    exit 1\n");
        sb.Append("fi\n");
        sb.Append("\n");
        sb.Append("echo \"使用的 Java: ${JAVA_EXE}\"\n");
        sb.Append("\"${JAVA_EXE}\" -version\n");
        sb.Append("echo\n");
        sb.Append("echo \"正在启动服务端 ${SERVER_JAR} ...\"\n");
        sb.Append("\n");
        sb.Append("exec \"${JAVA_EXE}\" ").Append(argumentLine).Append(" -jar \"${SERVER_JAR}\" nogui\n");

        return sb.ToString();
    }

    /// <summary>
    /// 把启动脚本写入目标目录。返回实际生成的文件名列表。
    /// </summary>
    public static List<string> WriteScripts(string serverDir, TargetPlatform platform, int memoryMb, bool includeAikarFlags, bool dryRun)
    {
        var serverJar = DetectServerJar(serverDir);
        var written = new List<string>();

        if (platform is TargetPlatform.Windows or TargetPlatform.Both)
        {
            var name = "start.bat";
            written.Add(name);
            if (!dryRun)
            {
                File.WriteAllText(Path.Combine(serverDir, name), BuildWindowsScript(serverJar, memoryMb, includeAikarFlags), new UTF8Encoding(false));
            }
        }

        if (platform is TargetPlatform.Unix or TargetPlatform.Both)
        {
            var name = "start.sh";
            written.Add(name);
            if (!dryRun)
            {
                var path = Path.Combine(serverDir, name);
                File.WriteAllText(path, BuildUnixScript(serverJar, memoryMb, includeAikarFlags), new UTF8Encoding(false));
                TryMakeExecutable(path);
            }
        }

        return written;
    }

    private static void TryMakeExecutable(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // 非 Unix 平台或权限不足时忽略。
        }
    }
}

/// <summary>启动脚本的目标平台。</summary>
public enum TargetPlatform
{
    Windows,
    Unix,
    Both
}
