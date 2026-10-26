using System.Diagnostics;
using Windows.Media.Control;

namespace AppleMusicPlugin;

/// <summary>
/// Talks to Apple Music through Windows' media session API (the same one behind the
/// media flyout), so commands go to Apple Music even if another app is also playing.
/// </summary>
sealed class AppleMusicMedia
{
    const string AppleMusicAppId = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";

    GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetSessions().FirstOrDefault(s => IsApple(s.SourceAppUserModelId));
    }

    static bool IsApple(string appId) =>
        appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
        appId.Contains("iTunes", StringComparison.OrdinalIgnoreCase);

    public static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    /// <summary>Pauses if playing, otherwise plays. Returns the new playing state, or null on failure.</summary>
    public async Task<bool?> PlayPauseAsync()
    {
        var session = await FindSessionAsync();
        if (session is null) return null;
        var wasPlaying = IsPlaying(session);
        var ok = wasPlaying ? await session.TryPauseAsync() : await session.TryPlayAsync();
        return ok ? !wasPlaying : null;
    }

    public async Task<bool> NextAsync()
    {
        var session = await FindSessionAsync();
        return session is not null && await session.TrySkipNextAsync();
    }

    public async Task<bool> PreviousAsync()
    {
        var session = await FindSessionAsync();
        return session is not null && await session.TrySkipPreviousAsync();
    }

    public static void LaunchApp() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{AppleMusicAppId}") { UseShellExecute = false })?.Dispose();
}
