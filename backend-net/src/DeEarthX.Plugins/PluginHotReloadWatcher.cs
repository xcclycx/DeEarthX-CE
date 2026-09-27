using System.Threading.Channels;
using DeEarthX.Core.Abstractions;
using Microsoft.Extensions.Hosting;
namespace DeEarthX.Plugins;

/// <summary>
/// 插件目录热重载：监听 <c>manifest.json</c> 与插件的增删，自动刷新插件缓存。
/// <para>
/// 这是“易开发性”的关键一环：改完清单或资源后无需重启宿主，
/// 插件列表与钩子执行计划会自动更新。监听不可用时（少数受限环境）只记录日志，
/// 手工调用 <c>POST /plugins/reload</c> 依然可用。
/// </para>
/// </summary>
public sealed class PluginHotReloadWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly PluginManager _pluginManager;
    private readonly ILogService _log;
    private readonly string _pluginsDir;

    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    private FileSystemWatcher? _watcher;
    private Task? _worker;
    private CancellationTokenSource? _cts;

    public PluginHotReloadWatcher(
        PluginManager pluginManager,
        IAppDirectoryProvider appDirectoryProvider,
        ILogService log)
    {
        _pluginManager = pluginManager;
        _log = log;
        _pluginsDir = Path.Combine(appDirectoryProvider.GetAppDirectory(), "plugins");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_pluginsDir);
        _pluginManager.EnableHotReload(true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => ProcessSignalsAsync(_cts.Token), CancellationToken.None);

        try
        {
            _watcher = new FileSystemWatcher(_pluginsDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                // 只关心插件清单；资源文件由用户点击“重新加载”或重启后生效，避免误报刷屏。
                Filter = "manifest.json",
                EnableRaisingEvents = false
            };

            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;

            _log.Info($"插件热重载已启用: {_pluginsDir}");
        }
        catch (Exception ex)
        {
            // 部分沙箱/受限文件系统不支持 FileSystemWatcher，降级为手工重载。
            _pluginManager.EnableHotReload(false);
            _log.Warn($"插件热重载不可用，请使用「重新加载」按钮: {ex.Message}");
            _watcher = null;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnChanged;
            _watcher.Error -= OnError;
        }

        _signals.Writer.TryComplete();
        _cts?.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // 安装/卸载过程中的临时目录不值得重建整个缓存。
        var name = Path.GetFileName(Path.GetDirectoryName(e.FullPath));
        if (!string.IsNullOrEmpty(name) && (name.StartsWith('_') || name.StartsWith('.')))
        {
            return;
        }

        _signals.Writer.TryWrite(true);
    }

    private void OnError(object sender, ErrorEventArgs e)
        => _log.Warn($"插件目录监听异常: {e.GetException().Message}");

    private async Task ProcessSignalsAsync(CancellationToken ct)
    {
        var reader = _signals.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // 合并短时间内的连续事件，避免一次保存触发多次全量重扫。
                while (reader.TryRead(out _))
                {
                }

                await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);

                while (reader.TryRead(out _))
                {
                }

                try
                {
                    var report = await _pluginManager.ReloadAllAsync(ct).ConfigureAwait(false);
                    _log.Info($"检测到插件变更，已自动重载: {report.Plugins.Count} 个插件，耗时 {report.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Error("插件自动重载失败", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cts?.Dispose();
    }
}
