using System.Collections.Concurrent;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

/// <summary>
/// 宿主与插件之间、插件与插件之间的解耦通信通道。
/// 发布方不关心订阅者是否存在；订阅者按注册顺序被依次调用。
/// </summary>
public interface IPluginEventBus
{
    /// <summary>注册一个处理器，返回的 <see cref="IDisposable"/> 用于退订。</summary>
    IDisposable Subscribe(string topic, PluginEventHandler handler, int priority = 0);

    /// <summary>是否已有订阅者。</summary>
    bool HasSubscribers(string topic);

    /// <summary>按优先级顺序同步派发，单个处理器异常不会中断其余处理器。</summary>
    Task PublishAsync(string topic, PluginEventArgs args, CancellationToken ct = default);
}

public delegate Task PluginEventHandler(PluginEventArgs args, CancellationToken ct);

public sealed class PluginEventArgs
{
    /// <summary>宿主发布的载荷；由发布方拥有，处理器只读。</summary>
    public object? Payload { get; init; }

    /// <summary>透传给插件的元数据（键值均为可序列化标量）。</summary>
    public IDictionary<string, object?> Meta { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// 可变的返回值槽：多个处理器可以依次改写它，形成“链式”扩展点
    /// （例如过滤策略返回 <c>filter = true</c>）。
    /// </summary>
    public IDictionary<string, object?> Result { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public bool Handled { get; set; }

    public string? Error { get; set; }
}

public sealed class PluginEventBus : IPluginEventBus
{
    private readonly ConcurrentDictionary<string, List<Registration>> _handlers = new(StringComparer.Ordinal);
    private readonly ILogService _log;

    public PluginEventBus(ILogService log) => _log = log;

    public IDisposable Subscribe(string topic, PluginEventHandler handler, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var registration = new Registration(handler, priority, Guid.NewGuid());
        var list = _handlers.GetOrAdd(topic, _ => new List<Registration>());

        lock (list)
        {
            list.Add(registration);
            list.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
        }

        return new Subscription(() =>
        {
            if (_handlers.TryGetValue(topic, out var current))
            {
                lock (current)
                {
                    current.RemoveAll(r => r.Token == registration.Token);
                }
            }
        });
    }

    public bool HasSubscribers(string topic)
        => _handlers.TryGetValue(topic, out var list) && list.Count > 0;

    public async Task PublishAsync(string topic, PluginEventArgs args, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic) || !_handlers.TryGetValue(topic, out var registrations))
        {
            return;
        }

        Registration[] snapshot;
        lock (registrations)
        {
            snapshot = registrations.ToArray();
        }

        foreach (var registration in snapshot)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await registration.Handler(args, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 单个插件出错不能拖垮整条发布链。
                _log.Error($"插件事件处理器异常: {topic}", ex);
                args.Error ??= ex.Message;
            }
        }
    }

    private sealed record Registration(PluginEventHandler Handler, int Priority, Guid Token);

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
