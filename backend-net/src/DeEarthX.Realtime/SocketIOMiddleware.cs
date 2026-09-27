using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeEarthX.Realtime;

public sealed class SocketIOMiddleware
{
    private const char EngineOpen = '0';
    private const char EngineClose = '1';
    private const char EnginePing = '2';
    private const char EnginePong = '3';
    private const char EngineMessage = '4';
    private const char EngineUpgrade = '5';
    private const char EngineNoop = '6';

    private const char SioConnect = '0';
    private const char SioDisconnect = '1';
    private const char SioEvent = '2';

    private const int ReceiveBufferSize = 8192;

    private readonly RequestDelegate _next;
    private readonly ILogService _log;

    public SocketIOMiddleware(RequestDelegate next, ILogService log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/socket.io"))
        {
            await _next(context);
            return;
        }

        var query = context.Request.Query;
        var transport = query["transport"].ToString();
        var sid = query["sid"].ToString();

        try
        {
            if (context.Request.Headers.Upgrade == "websocket" && transport == "websocket")
            {
                await HandleWebSocketAsync(context, sid);
            }
            else if (transport == "polling")
            {
                if (HttpMethods.IsGet(context.Request.Method))
                    await HandlePollingGetAsync(context, sid);
                else if (HttpMethods.IsPost(context.Request.Method))
                    await HandlePollingPostAsync(context, sid);
                else
                    context.Response.StatusCode = 400;
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("transport not supported");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error("Socket.IO 中间件异常", ex);
            if (!context.Response.HasStarted) context.Response.StatusCode = 500;
        }
    }

    private SocketIOServer GetServer(HttpContext ctx) => ctx.RequestServices.GetRequiredService<ISocketIOServer>() as SocketIOServer
        ?? throw new InvalidOperationException("SocketIOServer 未注册");

    private async Task HandlePollingGetAsync(HttpContext context, string sid)
    {
        var server = GetServer(context);
        context.Response.ContentType = "text/plain; charset=UTF-8";
        context.Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrEmpty(sid))
        {
            var newSid = server.CreateSession();
            var open = SocketIOServer.EncodeOpenFrame(newSid, new[] { "websocket" });
            var connect = "40" + JsonSerializer.Serialize(new { sid = server.TryGetSession(newSid, out var s0) ? s0.SocketIoSid : newSid }, DeEarthXJsonOptions.Default);
            await context.Response.WriteAsync(open + "\x1e" + connect);
            server.MarkSocketConnected(newSid);
            return;
        }

        if (!server.TryGetSession(sid, out var session))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("invalid sid");
            return;
        }

        session.LastPing = DateTime.UtcNow;
        var frame = await session.PollAsync(25000, context.RequestAborted);
        await context.Response.WriteAsync(frame ?? EngineNoop.ToString());
    }

    private async Task HandlePollingPostAsync(HttpContext context, string sid)
    {
        var server = GetServer(context);
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;

        var packets = body.Split('\x1e', StringSplitOptions.RemoveEmptyEntries);
        foreach (var packet in packets)
        {
            await ProcessEnginePacketAsync(server, sid, packet, null);
        }

        await context.Response.WriteAsync("{\"ok\":true}");
    }

    private async Task HandleWebSocketAsync(HttpContext context, string sid)
    {
        var server = GetServer(context);
        var isNew = string.IsNullOrEmpty(sid) || !server.TryGetSession(sid, out _);

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        SocketIOSession session;

        if (isNew)
        {
            var newSid = server.CreateSession();
            server.TryGetSession(newSid, out session!);

            var open = SocketIOServer.EncodeOpenFrame(newSid, Array.Empty<string>());
            await SendWsAsync(ws, open, context.RequestAborted);

            var connect = SocketIOServer.EncodeConnectFrame(session.SocketIoSid);
            await SendWsAsync(ws, connect, context.RequestAborted);
            server.MarkSocketConnected(newSid);
        }
        else
        {
            server.TryGetSession(sid, out session!);
        }

        await session.AttachWebSocketAsync(ws);

        _log.Info($"Socket.IO 客户端连接: sid={session.Sid} (websocket, connected={isNew})");

        var receiveBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                using var ms = new MemoryStream();
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", CancellationToken.None); }
                        catch { }
                        break;
                    }
                    ms.Write(receiveBuffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;

                var text = Encoding.UTF8.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
                var packets = text.Split('\x1e', StringSplitOptions.RemoveEmptyEntries);
                foreach (var packet in packets)
                {
                    await ProcessEnginePacketAsync(server, session.Sid, packet, ws);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { _log.Debug($"WebSocket 断开: {ex.Message}"); }
        catch (Exception ex) { _log.Error("WebSocket 接收循环异常", ex); }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
            session.DetachWebSocket(ws);
            if (!session.HasWebSocket)
            {
                server.RemoveSession(session.Sid);
                _log.Info($"Socket.IO 客户端断开: sid={session.Sid}");
            }
        }
    }

    private async Task ProcessEnginePacketAsync(SocketIOServer server, string sid, string packet, WebSocket? ws)
    {
        if (string.IsNullOrEmpty(packet)) return;
        var type = packet[0];
        var payload = packet.Length > 1 ? packet[1..] : "";

        switch (type)
        {
            case EnginePing:
                if (server.TryGetSession(sid, out var pingSession))
                    pingSession.LastPing = DateTime.UtcNow;
                if (ws is not null)
                    await SendWsAsync(ws, EnginePong.ToString(), CancellationToken.None);
                else if (server.TryGetSession(sid, out var pongSession))
                    await pongSession.DeliverFrameAsync(EnginePong.ToString());
                break;

            case EngineMessage:
                await ProcessSocketIoPacketAsync(server, sid, payload, ws);
                break;

            case EngineClose:
                server.RemoveSession(sid);
                break;

            case EngineUpgrade:
                break;
        }
    }

    private async Task ProcessSocketIoPacketAsync(SocketIOServer server, string sid, string data, WebSocket? ws)
    {
        if (string.IsNullOrEmpty(data)) return;
        var sioType = data[0];
        var rest = data.Length > 1 ? data[1..] : "";

        switch (sioType)
        {
            case SioConnect:
                server.MarkSocketConnected(sid);
                break;

            case SioDisconnect:
                server.RemoveSession(sid);
                break;

            case SioEvent:
                await ParseAndDispatchEventAsync(server, rest);
                break;
        }
    }

    private async Task ParseAndDispatchEventAsync(SocketIOServer server, string jsonArray)
    {
        int nsLen = 0;
        if (jsonArray.Length > 0 && jsonArray[0] == '/')
        {
            int commaIdx = jsonArray.IndexOf(',');
            if (commaIdx > 0) nsLen = commaIdx;
        }
        var jsonPart = nsLen > 0 ? jsonArray[(nsLen + 1)..] : jsonArray;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonPart); }
        catch { return; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return;

            var enumerator = root.EnumerateArray();
            if (!enumerator.MoveNext()) return;
            var eventName = enumerator.Current.GetString();
            if (string.IsNullOrEmpty(eventName)) return;

            JsonElement? arg = enumerator.MoveNext() ? enumerator.Current : null;
            await server.DispatchEventAsync(eventName!, arg);
        }
    }

    private static async Task SendWsAsync(WebSocket ws, string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
