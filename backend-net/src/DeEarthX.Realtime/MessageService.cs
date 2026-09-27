using System;
using System.Threading.Tasks;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Realtime;

public class MessageService : IMessageService
{
    private readonly ISocketIOServer _server;
    private readonly ILogService _log;

    public MessageService(ISocketIOServer server, ILogService log)
    {
        _server = server;
        _log = log;
    }

    public async Task SendAsync(string eventName, object? payload)
    {
        try
        {
            await _server.EmitAsync(eventName, payload);
        }
        catch (Exception ex)
        {
            _log.Error($"发送实时消息失败: {eventName}", ex);
        }
    }

    public Task Finish(long startTime, long endTime)
        => SendAsync("finish", endTime - startTime);

    public Task Unzip(string entryName, int total, int current)
        => SendAsync("unzip", new { name = entryName, total, current });

    public Task Download(int total, int index, string name)
        => SendAsync("downloading", new { total, index, name });

    public Task StatusChange()
        => SendAsync("changed", null);

    public Task HandleError(string message)
        => SendAsync("error", message);

    public Task Info(string message)
        => SendAsync("info", message);

    public Task ServerInstallStart(string modpackName, string minecraftVersion, string loaderType, string loaderVersion)
        => SendAsync("server_install_start", new { modpackName, minecraftVersion, loaderType, loaderVersion });

    public Task ServerInstallStep(string step, int stepIndex, int totalSteps, string? message = null)
        => SendAsync("server_install_step", new { step, stepIndex, totalSteps, message });

    public Task ServerInstallProgress(string step, int progress, string? message = null)
        => SendAsync("server_install_progress", new { step, progress, message });

    public Task ServerInstallComplete(string installPath, long duration)
        => SendAsync("server_install_complete", new { installPath, duration });

    public Task ServerInstallError(string error, string? step = null)
        => SendAsync("server_install_error", new { error, step });

    public Task FilterModsStart(int totalMods)
        => SendAsync("filter_mods_start", new { totalMods });

    public Task FilterModsProgress(int current, int total, string modName)
        => SendAsync("filter_mods_progress", new { current, total, modName });

    public Task FilterModsComplete(int filteredCount, int movedCount, long duration)
        => SendAsync("filter_mods_complete", new { filteredCount, movedCount, duration });

    public Task FilterModsError(string error)
        => SendAsync("filter_mods_error", new { error });

    public Task OptimizationReport(object report)
        => SendAsync("optimization_report", report);
}
