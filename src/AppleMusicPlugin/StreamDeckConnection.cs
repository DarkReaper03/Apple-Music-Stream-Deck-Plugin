using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppleMusicPlugin;

/// <summary>Minimal client for the Stream Deck plugin WebSocket protocol.</summary>
sealed class StreamDeckConnection : IDisposable
{
    readonly ClientWebSocket _socket = new();
    readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task ConnectAsync(int port, string pluginUuid, string registerEvent, CancellationToken ct)
    {
        await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), ct);
        await SendAsync(new JsonObject { ["event"] = registerEvent, ["uuid"] = pluginUuid });
    }

    /// <summary>Delivers each incoming event until Stream Deck closes the connection.</summary>
    public async Task ReceiveLoopAsync(Func<JsonObject, Task> onMessage, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = JsonNode.Parse(message.ToArray()) as JsonObject;
            message.SetLength(0);
            if (json is not null) await onMessage(json);
        }
    }

    public async Task SendAsync(JsonObject message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    Task Send(string ev, string context, JsonObject? payload = null)
    {
        var message = new JsonObject { ["event"] = ev, ["context"] = context };
        if (payload is not null) message["payload"] = payload;
        return SendAsync(message);
    }

    public Task SetTitleAsync(string context, string title) =>
        Send("setTitle", context, new JsonObject { ["title"] = title, ["target"] = 0 });

    public Task SetStateAsync(string context, int state) =>
        Send("setState", context, new JsonObject { ["state"] = state });

    public Task ShowAlertAsync(string context) => Send("showAlert", context);

    public Task ShowOkAsync(string context) => Send("showOk", context);

    public Task LogAsync(string text) =>
        SendAsync(new JsonObject { ["event"] = "logMessage", ["payload"] = new JsonObject { ["message"] = text } });

    public void Dispose()
    {
        _socket.Dispose();
        _sendLock.Dispose();
    }
}

static class JsonExtensions
{
    public static string? Str(this JsonNode? node, string key) =>
        node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? Int(this JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
        return null;
    }

    public static bool? Bool(this JsonNode? node, string key) =>
        node?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}
