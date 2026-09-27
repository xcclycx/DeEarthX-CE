using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Realtime;

public sealed class SocketIOServer : ISocketIOServer
{
    private static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    private readonly ILogService _log;
    private readonly ConcurrentDictionary<string, SocketIOSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<Func<JsonElement?, Task>>> _handlers = new();
    private readonly Timer _cleanupTimer;

    public SocketIOServer(ILogService log)
    {
        _log = log;
        _cleanupTimer = new Timer(_ => CleanupExpiredSessions(), null, CleanupInterval, CleanupInterval);
    }

    public int ClientCount => _sessions.Count;

    public void On(string eventName, Func<JsonElement?, Task> handler)
    {
        var list = _handlers.GetOrAdd(eventName, _ => new List<Func<JsonElement?, Task>>());
        lock (list) list.Add(handler);
    }

    public async Task EmitAsync(string eventName, object? payload)
    {
        if (_sessions.IsEmpty) return;

        var packet = EncodeEventFrame(eventName, payload);
        var tasks = _sessions.Values.Select(s => s.DeliverFrameAsync(packet));
        await Task.WhenAll(tasks);
    }

    internal string CreateSession()
    {
        var sid = GenerateSid();
        var socketIoSid = GenerateSid();
        var session = new SocketIOSession(sid, socketIoSid, _log);
        _sessions[sid] = session;
        return sid;
    }

    internal bool TryGetSession(string sid, out SocketIOSession session)
        => _sessions.TryGetValue(sid, out session!);

    internal bool RemoveSession(string sid)
        => _sessions.TryRemove(sid, out _);

    internal void MarkSocketConnected(string sid)
    {
        if (_sessions.TryGetValue(sid, out var s)) s.IsSocketConnected = true;
    }

    internal async Task DispatchEventAsync(string eventName, JsonElement? payload)
    {
        if (_handlers.TryGetValue(eventName, out var list))
        {
            List<Func<JsonElement?, Task>> snapshot;
            lock (list) snapshot = new List<Func<JsonElement?, Task>>(list);
            foreach (var h in snapshot)
            {
                try { await h(payload); }
                catch (Exception ex) { _log.Error($"处理 Socket.IO 事件 {eventName} 失败", ex); }
            }
        }
    }

    private void CleanupExpiredSessions()
    {
        try
        {
            foreach (var kvp in _sessions)
            {
                if (kvp.Value.IsExpired(SessionExpiry))
                {
                    if (_sessions.TryRemove(kvp.Key, out _))
                    {
                        _log.Debug($"已清理过期 Session: {kvp.Key}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("清理过期 Session 失败", ex);
        }
    }

    internal static string EncodeEventFrame(string eventName, object? payload)
    {
        string json;
        if (payload is null)
            json = "[\"" + eventName + "\"]";
        else
            json = "[\"" + eventName + "\"," + JsonSerializer.Serialize(payload, DeEarthXJsonOptions.Default) + "]";
        return "4" + "2" + json;
    }

    internal static string EncodeOpenFrame(string sid, string[] upgrades)
    {
        var handshake = new
        {
            sid,
            upgrades,
            pingInterval = 25000,
            pingTimeout = 20000,
            maxPayload = 1000000
        };
        return "0" + JsonSerializer.Serialize(handshake, DeEarthXJsonOptions.Default);
    }

    internal static string EncodeConnectFrame(string socketIoSid)
    {
        return "40" + JsonSerializer.Serialize(new { sid = socketIoSid }, DeEarthXJsonOptions.Default);
    }

    private static string GenerateSid()
    {
        var bytes = new byte[12];
        Random.Shared.NextBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
