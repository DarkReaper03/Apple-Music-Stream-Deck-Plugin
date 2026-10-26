using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AppleMusicPlugin;

/// <summary>Handles Stream Deck events for every Apple Music action and keeps the keys in sync.</summary>
sealed class Plugin(StreamDeckConnection sd)
{
    const string Prefix = "com.adamc.applemusic.";
    const string PlayPause = Prefix + "playpause";
    const string Next = Prefix + "next";
    const string Previous = Prefix + "previous";
    const string VolumeUp = Prefix + "volumeup";
    const string VolumeDown = Prefix + "volumedown";
    const string Mute = Prefix + "mute";

    const int DefaultStep = 5;

    /// <summary>A key currently on the Stream Deck, plus what we last drew on it (to skip redundant updates).</summary>
    sealed class Instance(string action, JsonObject settings, bool inMultiAction)
    {
        public string Action { get; } = action;
        public JsonObject Settings { get; set; } = settings;
        public bool InMultiAction { get; } = inMultiAction;
        public int? State { get; set; }
        public string? Title { get; set; }
    }

    readonly AppleMusicMedia _media = new();
    readonly Dictionary<string, Instance> _instances = [];
    // All event handling runs one item at a time on this queue, so no locking is needed elsewhere.
    readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
    CancellationTokenSource? _repeat;
    int _refreshQueued;
    // Windows reports the new play state a moment after a command; don't let polling flip the key back meanwhile.
    DateTime _holdPlayStateUntil;

    public async Task RunAsync(CancellationToken ct)
    {
        _ = WorkLoopAsync(ct);
        _ = PollLoopAsync(ct);
        await sd.ReceiveLoopAsync(message =>
        {
            Post(() => HandleAsync(message));
            return Task.CompletedTask;
        }, ct);
    }

    void Post(Func<Task> work) => _work.Writer.TryWrite(work);

    async Task WorkLoopAsync(CancellationToken ct)
    {
        await foreach (var work in _work.Reader.ReadAllAsync(ct))
        {
            try { await work(); }
            catch (Exception ex) { await sd.LogAsync($"Error: {ex}"); }
        }
    }

    async Task PollLoopAsync(CancellationToken ct)
    {
        // Picks up changes made outside the Stream Deck (Apple Music's own buttons, the volume mixer...).
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) continue;
            Post(async () =>
            {
                try { await RefreshAsync(); }
                finally { Volatile.Write(ref _refreshQueued, 0); }
            });
        }
    }

    async Task HandleAsync(JsonObject message)
    {
        var context = message.Str("context");
        var action = message.Str("action");
        var payload = message["payload"] as JsonObject;
        var settings = payload?["settings"] as JsonObject ?? [];
        if (context is null) return;

        switch (message.Str("event"))
        {
            case "willAppear" when action is not null:
                _instances[context] = new Instance(action, settings.DeepClone().AsObject(), payload.Bool("isInMultiAction") == true);
                await RefreshAsync();
                break;

            case "willDisappear":
                _instances.Remove(context);
                break;

            case "didReceiveSettings":
                if (_instances.TryGetValue(context, out var changed))
                {
                    changed.Settings = settings.DeepClone().AsObject();
                    await RefreshAsync();
                }
                break;

            case "keyDown" when action is not null:
                await KeyDownAsync(context, action, settings, payload.Bool("isInMultiAction") == true);
                break;

            case "keyUp":
                StopRepeat();
                break;
        }
    }

    async Task KeyDownAsync(string context, string action, JsonObject settings, bool inMultiAction)
    {
        switch (action)
        {
            case PlayPause:
                if (await _media.PlayPauseAsync() is bool nowPlaying)
                {
                    _holdPlayStateUntil = DateTime.UtcNow.AddSeconds(2);
                    await ApplyAllAsync(playing: nowPlaying, volume: null, volumeKnown: false);
                }
                else if (!AppVolume.IsAppRunning())
                {
                    AppleMusicMedia.LaunchApp();
                    await sd.ShowOkAsync(context);
                }
                else
                {
                    await sd.ShowAlertAsync(context);
                }
                break;

            case Next:
                if (!await _media.NextAsync()) await sd.ShowAlertAsync(context);
                break;

            case Previous:
                if (!await _media.PreviousAsync()) await sd.ShowAlertAsync(context);
                break;

            case VolumeUp:
            case VolumeDown:
                var step = Step(settings) * (action == VolumeUp ? 1 : -1);
                if (!await AdjustVolumeAsync(step))
                    await sd.ShowAlertAsync(context);
                else if (!inMultiAction) // multi-actions don't send keyUp, so never auto-repeat there
                    StartRepeat(step);
                break;

            case Mute:
                if (AppVolume.Get() is { } current && AppVolume.SetMute(!current.Muted))
                    await ApplyAllAsync(playing: null, volume: current with { Muted = !current.Muted }, volumeKnown: true);
                else
                    await sd.ShowAlertAsync(context);
                break;
        }
    }

    async Task<bool> AdjustVolumeAsync(int stepPercent)
    {
        if (AppVolume.Get() is not { } current) return false;
        var target = Math.Clamp(Percent(current.Level) + stepPercent, 0, 100);
        if (!AppVolume.SetLevel(target / 100f)) return false;
        await ApplyAllAsync(playing: null, volume: AppVolume.Get(), volumeKnown: true);
        return true;
    }

    /// <summary>Holding a volume key keeps stepping until it is released.</summary>
    void StartRepeat(int stepPercent)
    {
        StopRepeat();
        var cts = _repeat = new CancellationTokenSource(TimeSpan.FromSeconds(15)); // safety stop if keyUp is lost
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    Post(() => cts.IsCancellationRequested ? Task.CompletedTask : AdjustVolumeAsync(stepPercent));
                    await Task.Delay(120, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    void StopRepeat()
    {
        _repeat?.Cancel();
        _repeat = null;
    }

    async Task RefreshAsync()
    {
        if (_instances.Count == 0) return;

        bool? playing = null;
        if (DateTime.UtcNow >= _holdPlayStateUntil && _instances.Values.Any(i => i.Action == PlayPause))
            playing = await _media.FindSessionAsync() is { } session && AppleMusicMedia.IsPlaying(session);

        AppVolume.State? volume = null;
        var needsVolume = _instances.Values.Any(i => i.Action is VolumeUp or VolumeDown or Mute);
        if (needsVolume) volume = AppVolume.Get();

        await ApplyAllAsync(playing, volume, volumeKnown: needsVolume);
    }

    /// <summary>
    /// Redraws keys whose state changed. Play/pause keys are skipped when <paramref name="playing"/> is null,
    /// volume/mute keys when <paramref name="volumeKnown"/> is false (a null volume then means "no audio session").
    /// </summary>
    async Task ApplyAllAsync(bool? playing, AppVolume.State? volume, bool volumeKnown)
    {
        foreach (var (context, instance) in _instances)
        {
            if (instance.InMultiAction) continue;
            switch (instance.Action)
            {
                case PlayPause when playing is not null:
                    await SetStateAsync(context, instance, playing.Value ? 1 : 0);
                    break;

                case Mute when volumeKnown:
                    await SetStateAsync(context, instance, volume?.Muted == true ? 1 : 0);
                    break;

                case VolumeUp or VolumeDown when volumeKnown:
                    if (instance.Settings.Bool("showVolume") ?? true)
                        await SetTitleAsync(context, instance, volume is { } v ? $"{Percent(v.Level)}%" : "");
                    else if (instance.Title is not null)
                    {
                        // Hand the title back to whatever the user typed in Stream Deck.
                        await sd.SendAsync(new JsonObject { ["event"] = "setTitle", ["context"] = context, ["payload"] = new JsonObject { ["target"] = 0 } });
                        instance.Title = null;
                    }
                    break;
            }
        }
    }

    async Task SetStateAsync(string context, Instance instance, int state)
    {
        if (instance.State == state) return;
        instance.State = state;
        await sd.SetStateAsync(context, state);
    }

    async Task SetTitleAsync(string context, Instance instance, string title)
    {
        if (instance.Title == title) return;
        instance.Title = title;
        await sd.SetTitleAsync(context, title);
    }

    static int Step(JsonObject settings) => Math.Clamp(settings.Int("step") ?? DefaultStep, 1, 50);

    static int Percent(float level) => (int)Math.Round(level * 100);
}
