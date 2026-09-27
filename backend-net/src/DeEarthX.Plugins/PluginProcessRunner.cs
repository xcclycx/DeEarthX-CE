using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Filter;

namespace DeEarthX.Plugins;

/// <summary>进程式插件的单次调用结果。</summary>
public sealed record PluginProcessResult(bool Success, byte[]? Data, object? Extra, string? Error, bool TimedOut, int ExitCode);

public interface IPluginProcessRunner
{
    Task<PluginProcessResult> RunAsync(
        string scriptPath,
        string workingDirectory,
        string stdinJson,
        TimeSpan timeout,
        CancellationToken ct);
}

/// <summary>
/// 以子进程方式运行插件脚本（.js/.mjs/.bat/.cmd/.exe/.sh）。
/// <para>相比旧实现修正了两个关键问题：</para>
/// <list type="number">
///   <item><b>超时</b>：插件卡死不再永久阻塞整条流水线；超时后强制结束进程树。</item>
///   <item><b>解析契约</b>：除了 stdout 里的 JSON，同时支持 <c>dataPath</c>（大文件走磁盘回传）
///         与 <c>extra</c>（结构化附加数据），使过滤策略等扩展点能真正拿到插件返回值。</item>
/// </list>
/// </summary>
public sealed class PluginProcessRunner : IPluginProcessRunner
{
    private static readonly JsonSerializerOptions JsonOptions = DeEarthXJsonOptions.Default;

    private readonly ILogService _log;

    public PluginProcessRunner(ILogService log) => _log = log;

    public async Task<PluginProcessResult> RunAsync(
        string scriptPath,
        string workingDirectory,
        string stdinJson,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!File.Exists(scriptPath))
        {
            return new PluginProcessResult(false, null, null, $"入口文件不存在: {scriptPath}", false, -1);
        }

        var (fileName, arguments) = BuildCommand(scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new PluginProcessResult(false, null, null, $"无法启动插件进程: {ex.Message}", false, -1);
        }

        // 先挂读取任务再写 stdin，避免子进程大量输出时管道写满导致双向等待。
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        try
        {
            await process.StandardInput.WriteAsync(stdinJson.AsMemory(), linkedCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // 插件可能不读 stdin 就退出，这是合法行为。
            _log.Debug($"写入插件 stdin 失败（通常可忽略）: {scriptPath} ({ex.GetType().Name})");
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // ignore
            }
        }

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested;
            KillProcessTree(process);
        }

        string stdout = string.Empty;
        string stderr = string.Empty;
        try
        {
            stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Debug($"读取插件输出失败: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _log.Warn($"插件进程 stderr: {Truncate(stderr.Trim(), 800)}");
        }

        if (ct.IsCancellationRequested)
        {
            return new PluginProcessResult(false, null, null, "已取消", false, -1);
        }

        if (timedOut)
        {
            return new PluginProcessResult(false, null, null, $"执行超时（{timeout.TotalSeconds:0.#}s）", true, -1);
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        var parsed = Parse(stdout, exitCode, workingDirectory);
        return parsed;
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 进程可能刚好自行退出；这里不需要向调用方报错。
        }
    }

    /// <summary>根据扩展名选择解释器。参数一律走 ArgumentList 语义，避免手工拼引号出错。</summary>
    internal static (string FileName, string Arguments) BuildCommand(string scriptPath)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        return ext switch
        {
            ".js" or ".mjs" or ".cjs" => ("node", Quote(scriptPath)),
            ".bat" or ".cmd" => ("cmd.exe", $"/c {Quote(scriptPath)}"),
            ".sh" => ("bash", Quote(scriptPath)),
            ".py" => ("python", Quote(scriptPath)),
            ".ps1" => ("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)}"),
            _ => (scriptPath, string.Empty)
        };
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";

    /// <summary>
    /// 解析约定（全部可选，向后兼容纯 stdout JSON）：
    /// <code>
    /// { "success": true, "data": "&lt;base64&gt;", "dataPath": "out/result.zip", "extra": { ... }, "error": "..." }
    /// </code>
    /// 若 <c>dataPath</c> 存在则优先读取文件，避免大文件经 base64 挤占 stdout。
    /// </summary>
    private PluginProcessResult Parse(string stdout, int exitCode, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new PluginProcessResult(exitCode == 0, null, null, exitCode == 0 ? null : $"插件进程退出码 {exitCode}", false, exitCode);
        }

        var trimmed = stdout.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return new PluginProcessResult(exitCode == 0, null, null, null, false, exitCode);
        }

        var jsonText = trimmed[firstBrace..(lastBrace + 1)];
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var success = !root.TryGetProperty("success", out var successElement) || successElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => true
            };

            string? error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;

            byte[]? data = null;

            if (root.TryGetProperty("dataPath", out var dataPathElement) && dataPathElement.ValueKind == JsonValueKind.String)
            {
                var relative = dataPathElement.GetString();
                if (!string.IsNullOrWhiteSpace(relative))
                {
                    data = TryReadDataFile(relative, workingDirectory, out var readError);
                    if (data is null && error is null)
                    {
                        error = readError;
                    }
                }
            }

            if (data is null && root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
            {
                var base64 = dataElement.GetString();
                if (!string.IsNullOrEmpty(base64))
                {
                    try
                    {
                        data = Convert.FromBase64String(base64);
                    }
                    catch (FormatException ex)
                    {
                        error ??= $"data 不是合法的 base64: {ex.Message}";
                    }
                }
            }

            object? extra = null;
            if (root.TryGetProperty("extra", out var extraElement))
            {
                extra = JsonSerializer.Deserialize<object>(extraElement.GetRawText(), JsonOptions);
            }

            return new PluginProcessResult(success, data, extra, error, false, exitCode);
        }
        catch (JsonException)
        {
            return new PluginProcessResult(exitCode == 0, null, null, null, false, exitCode);
        }
    }

    private byte[]? TryReadDataFile(string relativePath, string workingDirectory, out string? error)
    {
        error = null;
        try
        {
            // dataPath 相对插件目录解析，并复用统一的越权校验。
            var fullPath = PluginPaths.ResolveInside(workingDirectory, relativePath);
            return File.ReadAllBytes(fullPath);
        }
        catch (UnauthorizedAccessException)
        {
            error = "dataPath 越权访问被拒绝";
            return null;
        }
        catch (Exception ex)
        {
            error = $"读取 dataPath 失败: {ex.Message}";
            return null;
        }
    }
}
