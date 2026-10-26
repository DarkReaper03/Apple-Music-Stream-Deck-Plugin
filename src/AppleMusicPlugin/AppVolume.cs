using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AppleMusicPlugin;

/// <summary>
/// Reads and changes Apple Music's own slider in the Windows volume mixer
/// (its per-app audio session), leaving the system volume untouched.
/// </summary>
static class AppVolume
{
    public readonly record struct State(float Level, bool Muted);

    static readonly string[] ProcessNames = ["AppleMusic", "iTunes"];
    static readonly string[] SessionIdMarkers = ["AppleInc.AppleMusicWin", "AppleInc.iTunes", "\\AppleMusic.exe", "\\iTunes.exe"];

    /// <summary>Current volume/mute of Apple Music, or null if it has no audio session yet.</summary>
    public static State? Get()
    {
        State? result = null;
        ForEachAppleSession(volume =>
        {
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);
            result = new State(level, muted);
            return false; // first session is enough
        });
        return result;
    }

    /// <summary>Sets the volume (0..1) on every Apple Music session. Returns false if none exist.</summary>
    public static bool SetLevel(float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        var ctx = Guid.Empty;
        return ForEachAppleSession(volume =>
        {
            volume.SetMasterVolume(level, ref ctx);
            // Turning the volume up should also undo a mute, like the Windows mixer does.
            if (level > 0) volume.SetMute(false, ref ctx);
            return true;
        });
    }

    public static bool SetMute(bool muted)
    {
        var ctx = Guid.Empty;
        return ForEachAppleSession(volume =>
        {
            volume.SetMute(muted, ref ctx);
            return true;
        });
    }

    /// <summary>
    /// Runs <paramref name="action"/> for each Apple Music session on every active output device
    /// (Apple Music may not be on the default one). The callback returns false to stop early.
    /// </summary>
    static bool ForEachAppleSession(Func<ISimpleAudioVolume, bool> action)
    {
        var found = false;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out var devices) != 0) return false;
            devices.GetCount(out var deviceCount);
            for (uint d = 0; d < deviceCount; d++)
            {
                if (devices.Item(d, out var device) != 0) continue;
                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var managerObj) != 0) continue;
                var manager = (IAudioSessionManager2)managerObj;
                if (manager.GetSessionEnumerator(out var sessions) != 0) continue;
                sessions.GetCount(out var sessionCount);
                for (var s = 0; s < sessionCount; s++)
                {
                    if (sessions.GetSession(s, out var session) != 0) continue;
                    if (!IsAppleSession(session)) continue;
                    found = true;
                    if (!action((ISimpleAudioVolume)session)) return true;
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(enumerator);
        }
        return found;
    }

    static bool IsAppleSession(IAudioSessionControl2 session)
    {
        if (session.GetProcessId(out var pid) == 0 && pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (ProcessNames.Any(n => process.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch (ArgumentException) { /* process already exited */ }
        }
        // Fallback: the session identifier embeds the executable path of the owning app.
        return session.GetSessionIdentifier(out var id) == 0 && id is not null &&
               SessionIdMarkers.Any(m => id.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True if the Apple Music (or iTunes) app process is running at all.</summary>
    public static bool IsAppRunning() =>
        ProcessNames.Any(n =>
        {
            var processes = Process.GetProcessesByName(n);
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        });

    // --- Core Audio COM interop -------------------------------------------------------------
    // Interfaces must list every method in vtable order (inherited ones included), up to the last one used.

    const uint DeviceStateActive = 0x1;
    const int ClsCtxAll = 0x17;

    enum EDataFlow { Render = 0, Capture = 1, All = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
