namespace AppleMusicPlugin;

/// <summary>
/// Exercises the Apple Music controls without Stream Deck, e.g. `AppleMusicPlugin.exe --test status`.
/// Commands: status | playpause | next | previous | volume &lt;0-100&gt; | mute | unmute
/// </summary>
static class SelfTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var media = new AppleMusicMedia();
        var command = args.FirstOrDefault() ?? "status";
        bool ok;

        switch (command)
        {
            case "status":
                ok = true;
                break;
            case "playpause":
                var playing = await media.PlayPauseAsync();
                ok = playing is not null;
                Console.WriteLine($"playpause -> {(playing is null ? "failed" : playing.Value ? "playing" : "paused")}");
                break;
            case "next":
                ok = await media.NextAsync();
                break;
            case "previous":
                ok = await media.PreviousAsync();
                break;
            case "volume" when args.Length > 1 && int.TryParse(args[1], out var percent):
                ok = AppVolume.SetLevel(percent / 100f);
                break;
            case "mute":
            case "unmute":
                ok = AppVolume.SetMute(command == "mute");
                break;
            default:
                Console.WriteLine("usage: --test status|playpause|next|previous|volume <0-100>|mute|unmute");
                return 2;
        }

        if (command != "status") await Task.Delay(500); // let Windows catch up before reporting
        var session = await media.FindSessionAsync();
        var volume = AppVolume.Get();
        Console.WriteLine($"command:      {command} -> {(ok ? "ok" : "FAILED")}");
        Console.WriteLine($"app running:  {AppVolume.IsAppRunning()}");
        Console.WriteLine($"media session: {session?.SourceAppUserModelId ?? "(none)"}");
        if (session is not null)
        {
            var props = await session.TryGetMediaPropertiesAsync();
            Console.WriteLine($"playing:      {AppleMusicMedia.IsPlaying(session)}");
            Console.WriteLine($"track:        {props?.Title} - {props?.Artist}");
        }
        Console.WriteLine($"volume:       {(volume is { } v ? $"{Math.Round(v.Level * 100)}%{(v.Muted ? " (muted)" : "")}" : "(no audio session)")}");
        return ok ? 0 : 1;
    }
}
