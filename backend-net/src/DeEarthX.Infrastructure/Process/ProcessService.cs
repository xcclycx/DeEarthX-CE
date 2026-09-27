using System.Diagnostics;
using System.Text;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Infrastructure.Process;

public interface IProcessService
{
    Task<int> RunAsync(string command, string? workingDir = null, CancellationToken ct = default);

    Task<(int ExitCode, string Output)> RunCaptureAsync(string command, string? workingDir = null, CancellationToken ct = default);
}

public sealed class ProcessService : IProcessService
{
    private readonly ILogService _log;

    public ProcessService(ILogService log)
    {
        _log = log;
    }

    public Task<int> RunAsync(string command, string? workingDir = null, CancellationToken ct = default)
    {
        return RunCoreAsync(command, workingDir, capture: false, ct);
    }

    public Task<(int ExitCode, string Output)> RunCaptureAsync(string command, string? workingDir = null, CancellationToken ct = default)
    {
        return RunCaptureCoreAsync(command, workingDir, ct);
    }

    private async Task<int> RunCoreAsync(string command, string? workingDir, bool capture, CancellationToken ct)
    {
        SafeLogDebug($"执行命令: {command}");
        var psi = BuildStartInfo(command, workingDir, redirect: true);

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            SafeLogDebug(e.Data.Trim());
            if (capture) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _log.Error(e.Data.Trim());
            stderrBuilder.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程: {command}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var _ = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var code = process.ExitCode;
        SafeLogDebug($"命令执行完成，退出码: {code}");

        if (code != 0)
        {
            throw new InvalidOperationException($"Command failed with exit code {code}");
        }

        return code;
    }

    private async Task<(int ExitCode, string Output)> RunCaptureCoreAsync(string command, string? workingDir, CancellationToken ct)
    {
        SafeLogDebug($"执行命令: {command}");
        var psi = BuildStartInfo(command, workingDir, redirect: true);

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderrBuilder.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程: {command}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var _ = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var output = new StringBuilder();
        output.Append(stdoutBuilder);
        output.Append(stderrBuilder);
        return (process.ExitCode, output.ToString());
    }

    private static ProcessStartInfo BuildStartInfo(string command, string? workingDir, bool redirect)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(workingDir))
        {
            psi.WorkingDirectory = workingDir;
        }

        return psi;
    }

    private void SafeLogDebug(string message)
    {
        try
        {
            _log.Debug(message);
        }
        catch
        {
        }
    }
}
