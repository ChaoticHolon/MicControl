# MicControlNG

**MicControl Next Generation** (MicControlNG): mute and unmute your microphone system-wide with a hotkey, with an always-on-top overlay, sounds and a tray icon. It works with every app that uses the microphone: Discord, Teams, Zoom, games and more.

MicControl is the successor of [MicSwitch](https://github.com/iXab3r/MicSwitch) by Xab3r, rebuilt on .NET 10 and Avalonia, with explicit plans to eventually release it cross-platform as the original developer intended. MicSwitch settings are imported automatically.

## Features
- Multiple microphones support (useful for streamers) - ALL microphones in your system could be muted/unmuted by a single key press
- System-wide hotkeys (supports mouse XButtons)
- Always-on-top configurable (scale, transparency) Overlay - could be disable if not needed
- Mute/unmute audio notification (with custom audio files support)
- Customizable tray and overlay icons
- Multiple hotkeys support
- Auto-startup (could be Minimized by default)
- Three Audio modes: Push-to-talk, Push-to-mute and Toggle mute
- Overlay visibility could be linked to microphone state, i.e. it will be shown only when Muted/Unmuted
- Auto-updates via GitHub Releases
- Light and dark theme (follows Windows by default)

## How to build

Requirements: Windows 10 (version 2004 or later) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (included with Visual Studio 2026's ".NET desktop development" workload).

```
git clone https://github.com/ChaoticHolon/MicControlNG.git
cd MicControlNG/Sources
dotnet build MicControlNG.slnx
dotnet test --project MicControlNG.Tests
dotnet run --project MicControlNG
```

In Visual Studio, open `Sources\MicControlNG.slnx`, set **MicControlNG** as the startup project and run. Start Visual Studio as administrator: the app requests elevation so hotkeys keep working in elevated games.

The UI is built with [Avalonia](https://avaloniaui.net) so it can run on Linux soon (KDE Plasma 6 on Wayland is the planned first target). The tests, including headless UI tests, run on Windows and Linux.

### Project layout
| Project | Contents |
| --- | --- |
| `MicControlNG.Core` | Platform-independent logic and contracts: settings model and JSON storage, MicSwitch import, hotkey text format, mute rules, notification sound library, and the `MicControlNG.Platform` interfaces each OS implements |
| `MicControlNG.UI` | Avalonia 12 UI (Fluent theme, light/dark): views, view models (CommunityToolkit.Mvvm), tray icon, app composition |
| `MicControlNG.Windows` | Windows implementations: Core Audio mute/volume (NAudio), low-level keyboard/mouse hooks, WASAPI sound playback, start with Windows, overlay click-through |
| `MicControlNG` | Windows executable (`MicControlNG.exe`): wires the UI to the Windows services; single instance; Velopack updates |
| `MicControlNG.Tests` | xUnit v3 tests: core logic plus headless UI tests against fake platform services |

### Settings
Settings are stored in `%APPDATA%\MicControlNG\settings.json`, logs in `%APPDATA%\MicControlNG\logs`. On first start, MicControl imports from `%APPDATA%\MicSwitch`: settings, custom icons and sounds from recent builds, or a MicSwitch 1.x `release\config.cfg`. MicSwitch autostart entries are replaced with MicControlNG ones.
