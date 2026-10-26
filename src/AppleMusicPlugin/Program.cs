using AppleMusicPlugin;

// Stream Deck launches the plugin as: -port <n> -pluginUUID <id> -registerEvent <event> -info <json>
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];

if (args.Length > 0 && args[0] == "--test")
    return await SelfTest.RunAsync(args.Skip(1).ToArray());

if (!options.TryGetValue("-port", out var portText) || !int.TryParse(portText, out var port) ||
    !options.TryGetValue("-pluginUUID", out var pluginUuid) ||
    !options.TryGetValue("-registerEvent", out var registerEvent))
{
    return 1; // not started by Stream Deck
}

using var cts = new CancellationTokenSource();
using var sd = new StreamDeckConnection();
await sd.ConnectAsync(port, pluginUuid, registerEvent, cts.Token);
await new Plugin(sd).RunAsync(cts.Token);
cts.Cancel();
return 0;
