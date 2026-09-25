# MicControl (MicControlNG)

System-wide microphone mute/unmute with hotkeys, an always-on-top overlay, sounds and a tray icon. Successor of MicSwitch by Xab3r.
- **Display name** (UI, tray, notices): "MicControl" (`MainViewModel.AppName`); full name "MicControl Next Generation" (`ProductFullName`).
- **Technical name** (repo, solution, projects, namespaces, exe, settings folder, mutex, autostart entries): `MicControlNG`.
- Keep "MicSwitch" only for compatibility: importing `%APPDATA%\MicSwitch` (`SettingsStore` legacy folder, `LegacyConfigMigrator` 1.x type names), removing old autostart entries, and the credit "Based on MicSwitch by Xab3r".
.NET 10, Avalonia 12.1 UI, C# latest. Solution: `Sources/MicControlNG.slnx`.

## Platform scope
- **Windows 10 (2004+) is the only supported platform right now.** Windows 11 gets tested later in a VM.
- **Linux is next: KDE Plasma 6 on Wayland only** (no X11, no other desktops for now). Plan: GlobalShortcuts portal for hotkeys, PipeWire for audio, layer-shell overlay, XDG autostart, Flatpak.
- macOS is a distant stretch goal. Don't use MAUI.
- The user works in **Visual Studio Enterprise** only; never suggest VS Code or JetBrains tooling.

## Commands (run from `Sources/`)
- Build: `dotnet build MicControlNG.slnx`. Warnings are errors, and the analyzers are strict (`AnalysisLevel=latest-recommended`).
- Test: `dotnet test --project MicControlNG.Tests` (Microsoft Testing Platform via `global.json`; the old `dotnet test <proj>` form fails).
- Run (Windows): `dotnet run --project MicControlNG`. The app asks for elevation (`highestAvailable`), so run from an elevated terminal or Visual Studio started as administrator.
- Tests run on Windows and Linux. The Windows projects also build on Linux (`EnableWindowsTargeting`).

## Layout
| Project | Contents | Rules |
| --- | --- | --- |
| `MicControlNG.Core` (net10.0) | Settings model and JSON store (`Settings/`), 1.x config import, `HotkeyGesture` text format, `MuteRules`, `NotificationSounds`, platform contracts (`Platform/PlatformServices.cs`) | No UI or OS dependencies |
| `MicControlNG.UI` (net10.0) | Avalonia UI: sidebar shell `Views/MainWindow`, tray `Views/TrayIcon` + `Views/TrayPopupWindow`, pages in `Views/Pages/` (Home, Extra hotkeys, Sounds, Overlay, About, first-run Setup), `OverlayWindow` + `Controls/OverlayBadge`, `TrayIcon` (right-click menu), one `MainViewModel` split into partial files per page, theme in `Themes/`, composition in `App.axaml.cs` | Talk to the OS only through the `MicControlNG.Platform` interfaces |
| `MicControlNG.Windows` | Core Audio via NAudio, low-level keyboard/mouse hooks (`GlobalHotkeyService`, `VirtualKeys`), WASAPI playback, autostart (Run key or scheduled task), overlay click-through | Windows-only code goes here |
| `MicControlNG` | Windows exe (`MicControlNG.exe`): `Program.cs` (single instance, DI registration of Windows services), Velopack `UpdateService`, `app.manifest` | |
| `MicControlNG.Tests` | xUnit v3: core tests plus headless Avalonia UI tests (`Ui/`, with fakes in `Ui/Fakes.cs`) | |

## Product decisions (from the owner)
- Default mode is Push-to-talk; Toggle and Push-to-mute are alternatives. "Exclusive hotkey" (suppress) is off by default.
- App mode (`window.appMode`): **Tray** (default) starts hidden, including at sign-in; Close and Minimize hide to the tray, with a once-per-session notice that has "Don't show again". **Window** is a normal app: Close exits, Minimize goes to the taskbar. First run always shows the window, for setup.
- The tray icon is always shown. Left-click never changes state; it opens the quick popup (`TrayPopupWindow`: state, Mute button, level meter, microphone and mode, "Open full window"), positioned only from the screen work area (`PopupPlacement`) so KDE Plasma can reuse it. Right-click: Open · Mute/Unmute · Mode · Microphone · Overlay · Play sounds · App mode · Start with Windows · Exit.
- Extra hotkeys are a dynamic list of key → action rows; the action list hides whatever the main key already does in the current mode.
- Overlay is a translucent Discord-style badge, positioned by corner presets or "Edit position" (drag); click-through otherwise.
- The level meter and speaking indicator open the mic (OS "in use" indicator), so the meter only runs while Home/Setup is visible and the speaking indicator is opt-in.
- Notification sounds must be CC0/public-domain (e.g. Kenney, Freesound CC0), not Pixabay.

## Conventions
- Package versions live only in `Sources/Directory.Packages.props`; shared build settings are in `Directory.Build.props`, style in `.editorconfig`.
- XAML uses compiled bindings (`x:DataType`). For a ComboBox, use `DisplayMemberBinding` and `SelectedValueBinding` with `x:DataType`. Selected values must never be `null`: use `""` for "None" or "Default".
- Theme colors are our own light/dark `ThemeDictionaries` in `Themes/Theme.axaml`, so Windows 10 and 11 look identical. Icons use the `IconFont` resource (Segoe Fluent Icons, falling back to Segoe MDL2 Assets). Linux will need a bundled icon font.
- Settings: `%APPDATA%\MicControlNG\settings.json` (camelCase, hotkeys stored as text like `"Ctrl+F1"`); logs in `%APPDATA%\MicControlNG\logs`. Keep the MicSwitch import working (legacy folder and 1.x `LegacyConfigMigrator`), and keep hotkey strings compatible with 1.x.
- Every new platform capability gets an interface in `MicControlNG.Core/Platform`, a Windows implementation, a fake in the tests, and a flag in `PlatformCapabilities` if some platforms can't support it.
- Add or extend tests for behavior changes; UI behavior can be tested headlessly with `[AvaloniaFact]`.
