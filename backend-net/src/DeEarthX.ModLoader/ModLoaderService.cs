using DeEarthX.Core.Abstractions;
using DeEarthX.Realtime;

namespace DeEarthX.ModLoader;

public interface IModLoaderService
{
    Task MlSetupAsync(
        string ml,
        string mcv,
        string mlv,
        string path,
        IMessageService? message,
        string? template,
        CancellationToken ct = default);

    Task DInstallAsync(
        string ml,
        string mcv,
        string mlv,
        string path,
        CancellationToken ct = default);
}

public sealed class ModLoaderService : IModLoaderService
{
    private readonly ModLoaderFactory _factory;
    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly ILogService _log;

    public ModLoaderService(
        ModLoaderFactory factory,
        IAppDirectoryProvider appDirectoryProvider,
        ILogService log)
    {
        _factory = factory;
        _appDirectoryProvider = appDirectoryProvider;
        _log = log;
    }

    public async Task MlSetupAsync(
        string ml,
        string mcv,
        string mlv,
        string path,
        IMessageService? message,
        string? template,
        CancellationToken ct = default)
    {
        var totalSteps = template is not null && template != "0" ? 1 : (template is not null ? 3 : 2);

        try
        {
            if (message is not null)
            {
                await message.ServerInstallStart("Server Installation", mcv, ml, mlv).ConfigureAwait(false);
            }

            if (template is not null && template != "0")
            {
                if (message is not null)
                {
                    await message.ServerInstallStep($"Applying Template: {template}", 1, totalSteps).ConfigureAwait(false);
                }

                var templateDataPath = Path.Combine(_appDirectoryProvider.GetAppDirectory(), "templates", template, "data");
                try
                {
                    if (Directory.Exists(templateDataPath))
                    {
                        await CopyDirectoryAsync(templateDataPath, path, ct).ConfigureAwait(false);
                        if (message is not null)
                        {
                            await message.ServerInstallProgress($"Applied Template: {template}", 100).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        _log.Warn($"Template {template} not found");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"应用模板 {template} 失败", ex);
                    if (message is not null)
                    {
                        await SafeNotifyAsync(() => message.ServerInstallError($"Failed to apply template: {ex.Message}")).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (message is not null)
                {
                    await message.ServerInstallStep("Installing Minecraft Server", 1, totalSteps).ConfigureAwait(false);
                }

                await _factory.CreateMinecraft(ml, mcv, mlv, path).SetupAsync(ct).ConfigureAwait(false);

                if (message is not null)
                {
                    await message.ServerInstallProgress("Installing Minecraft Server", 100).ConfigureAwait(false);
                    await message.ServerInstallStep($"Installing {ml} Loader", 2, totalSteps).ConfigureAwait(false);
                }

                await _factory.Create(ml, mcv, mlv, path).SetupAsync(ct).ConfigureAwait(false);

                if (message is not null)
                {
                    await message.ServerInstallProgress($"Installing {ml} Loader", 100).ConfigureAwait(false);
                }

                if (template == "0" && message is not null)
                {
                    await message.ServerInstallStep("No template selected, using official mod loader", 3, totalSteps).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            if (message is not null)
            {
                await SafeNotifyAsync(() => message.ServerInstallError(ex.Message)).ConfigureAwait(false);
            }
            throw;
        }
    }

    public async Task DInstallAsync(
        string ml,
        string mcv,
        string mlv,
        string path,
        CancellationToken ct = default)
    {
        await _factory.Create(ml, mcv, mlv, path).InstallerAsync(ct).ConfigureAwait(false);

        var cmd = string.Empty;
        if (ml == "forge")
        {
            cmd = $"java -jar forge-{mcv}-{mlv}-installer.jar --installServer";
        }
        else if (ml == "neoforge")
        {
            cmd = $"java -jar neoforge-{mcv}-{mlv}-installer.jar --installServer";
        }
        else if (ml == "fabric" || ml == "fabric-loader")
        {
            Directory.CreateDirectory(path);

            var runBat = "@echo off" + Environment.NewLine + "java -jar fabric-server-launch.jar" + Environment.NewLine;
            var runSh = "#!/bin/bash\njava -jar fabric-server-launch.jar\n";
            await File.WriteAllTextAsync(Path.Combine(path, "run.bat"), runBat, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(path, "run.sh"), runSh, ct).ConfigureAwait(false);

            cmd = $"java -jar fabric-installer.jar server -dir . -mcversion {mcv} -loader {mlv} -downloadMinecraft";
        }

        if (!string.IsNullOrEmpty(cmd))
        {
            Directory.CreateDirectory(path);

            var installBat = "@echo off" + Environment.NewLine + cmd + Environment.NewLine +
                             "echo Install Successfully,Enter Some Key to Exit!" + Environment.NewLine +
                             "pause" + Environment.NewLine;
            var installSh = "#!/bin/bash\n" + cmd + "\n";

            await File.WriteAllTextAsync(Path.Combine(path, "install.bat"), installBat, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(path, "install.sh"), installSh, ct).ConfigureAwait(false);
        }
    }

    private static async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relative);
            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent))
            {
                Directory.CreateDirectory(destParent);
            }
            await CopyFileAsync(file, destPath, ct).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await source.CopyToAsync(dest, 81920, ct).ConfigureAwait(false);
    }

    private static async Task SafeNotifyAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
