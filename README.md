# Apple Music Controls for Stream Deck (Windows)

> [!WARNING]
> **This project is no longer maintained.** It is provided as-is. Issues and pull requests won't be answered, and no further updates are planned. You're welcome to fork it under the terms of the GPL-3.0.

Stream Deck plugin for the Apple Music app on Windows (iTunes works too).

![Key icons](docs/keys.png)

| Action | What it does |
| --- | --- |
| **Play / Pause** | Toggles playback. The key shows ▶ or ❚❚ to match Apple Music. Opens Apple Music if it isn't running. |
| **Next Track** / **Previous Track** | Skips songs. |
| **Volume Up** / **Volume Down** | Changes Apple Music's own slider in the Windows volume mixer, not the system volume. Hold to keep changing. Shows the current % on the key. |
| **Mute** | Mutes or unmutes Apple Music only. |

Commands go straight to Apple Music through Windows' media session API, so they still reach Apple Music when another app (a browser, Spotify) is also playing.

## Requirements

- Windows 10 or 11
- Stream Deck software 6.5 or newer
- The Apple Music app from the Microsoft Store (or iTunes)

## Install

1. Download `com.adamc.applemusic.streamDeckPlugin` from the [latest release](https://github.com/DarkReaper03/Apple-Music-Stream-Deck-Plugin/releases/latest).
2. Double-click it. Stream Deck opens and asks to install the plugin.
3. In Stream Deck, find the **Apple Music** category in the action list and drag the actions onto keys.

Nothing else is needed. The download includes everything required to run, including .NET.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Clone the repo, then run this from its folder in PowerShell:

```powershell
.\build.ps1
```

If PowerShell refuses to run the script, use `powershell -ExecutionPolicy Bypass -File .\build.ps1` instead.

This generates the icons, builds `AppleMusicPlugin.exe` into `com.adamc.applemusic.sdPlugin\bin`, copies the plugin into `%APPDATA%\Elgato\StreamDeck\Plugins`, and restarts Stream Deck. Use `-NoInstall` to build without installing, or `-Package` to build the self-contained `dist\com.adamc.applemusic.streamDeckPlugin` used for releases.

Volume keys have a step size (1–20%) and a "Show volume on key" option.

## Notes

- If a volume key shows a ⚠, Apple Music doesn't have an audio session yet. Windows only creates one after the app has played sound. Start a song and try again.
- To check the controls without Stream Deck:
  `com.adamc.applemusic.sdPlugin\bin\AppleMusicPlugin.exe --test status` (also `playpause`, `next`, `previous`, `volume 50`, `mute`, `unmute`). It's a windowless app, so capture its output with `Start-Process ... -Wait -NoNewWindow -RedirectStandardOutput out.txt`.
- Errors are logged to `%APPDATA%\Elgato\StreamDeck\logs\com.adamc.applemusic0.log`.

## Layout

- `src/AppleMusicPlugin/`: the plugin (C#)
  - `Plugin.cs`: Stream Deck event handling and key updates
  - `AppleMusicMedia.cs`: play/pause/skip through Windows media sessions
  - `AppVolume.cs`: per-app volume through Windows Core Audio
  - `StreamDeckConnection.cs`: Stream Deck WebSocket protocol
- `com.adamc.applemusic.sdPlugin/`: the plugin bundle (manifest, settings page, generated icons, built exe)
- `tools/make-icons.ps1`: draws every icon

## License

Copyright (C) 2026 DarkReaper03

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY. See [LICENSE](LICENSE) for the full text.

Not affiliated with or endorsed by Apple or Elgato. Apple Music and iTunes are trademarks of Apple Inc. Stream Deck is a trademark of Corsair Memory, Inc.
