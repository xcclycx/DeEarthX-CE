using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Realtime;

internal sealed class SocketIOSession
{
    private const int MaxPollingBufferSize = 1024;

    public string Sid { get; }
    public string SocketIoSid { get; }
    public DateTime LastPing { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    private readonly ConcurrentQueue<string> _pollingBuffer = new();
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _webSockets = new();
    private readonly SemaphoreSlim _pollSignal = new(0);
    private readonly ILogService? _log;

    public bool IsSocketConnected { get; set; }
    public bool HasWebSocket => !_webSockets.IsEmpty;

    public SocketIOSession(string sid, string socketIoSid, ILogService? log = null)
    {
        Sid = sid;
        SocketIoSid = socketIoSid;
        LastPing = DateTime.UtcNow;
        _log = log;
    }

    public async Task DeliverFrameAsync(string frame)
    {
        if (_webSockets.IsEmpty)
        {
            if (_pollingBuffer.Count >= MaxPollingBufferSize)
            {
                _log?.Warn($"Session {Sid} polling buffer 已满 ({MaxPollingBufferSize})，丢弃最旧帧");
                _pollingBuffer.TryDequeue(out _);
            }
            _pollingBuffer.Enqueue(frame);
            try { _pollSignal.Release(); } catch (SemaphoreFullException) { }
            return;
        }

        var frameBytes = Encoding.UTF8.GetBytes(frame);
        foreach (var kvp in _webSockets)
        {
            var ws = kvp.Key;
            var sem = kvp.Value;
            try
            {
                await sem.WaitAsync();
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(frameBytes),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                finally { sem.Release(); }
            }
            catch (WebSocketException ex)
            {
                _log?.Debug($"Session {Sid} WebSocket 发送失败: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                _log?.Debug($"Session {Sid} WebSocket 已关闭");
            }
            catch (Exception ex)
            {
                _log?.Warn($"Session {Sid} WebSocket 发送异常", ex);
            }
        }
    }

    public async Task AttachWebSocketAsync(WebSocket ws)
    {
        var sem = new SemaphoreSlim(1, 1);
        _webSockets[ws] = sem;

        await sem.WaitAsync();
        try
        {
            while (_pollingBuffer.TryDequeue(out var buffered))
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(buffered)),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (WebSocketException ex)
                {
                    _log?.Debug($"Session {Sid} 刷送缓冲帧失败: {ex.Message}");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        finally { sem.Release(); }
    }

    public void DetachWebSocket(WebSocket ws)
    {
        if (_webSockets.TryRemove(ws, out var sem))
        {
            sem.Dispose();
        }
    }

    public async Task<string?> PollAsync(int timeoutMs, CancellationToken ct)
    {
        if (_pollingBuffer.TryDequeue(out var immediate)) return immediate;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await _pollSignal.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        return _pollingBuffer.TryDequeue(out var frame) ? frame : null;
    }

    public bool IsExpired(TimeSpan timeout)
    {
        if (HasWebSocket) return false;
        return DateTime.UtcNow - LastPing > timeout;
    }
}
