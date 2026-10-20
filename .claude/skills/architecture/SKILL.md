---
name: architecture
description: MicControl's project layout and code conventions in full - what each project (Core, UI, Windows, exe, Tests) contains and may depend on, XAML compiled bindings and ComboBox null rules, theme and icon font, settings and log files, platform capability interfaces, tests and logging levels. Load before adding code to a project, a new platform capability, XAML, settings fields or log messages.
---

# Architecture and conventions

## Layout
| Project | Contents | Rules |
| --- | --- | --- |
| `MicControl.Core` (net10.0) | Settings model and JSON store (`Settings/`), MicSwitch 1.x config import, `HotkeyGesture` text format, `MuteRules`, `NotificationSounds`, update feed parsing and trusted release keys (`Updates/ReleaseFeed`, `ReleaseKeys`), crypto helpers (`Security/`: `Ed25519` verify, `SshSignature`, `SecretBox`), setup command line and install layout (`Installation/`), platform contracts (`Platform/PlatformServices.cs`) | No UI or OS dependencies |
| `MicControl.UI` (net10.0) | Avalonia UI: `Views/MainWindow` (custom 52 px header in place of the system title bar; see `Sources/MicControl.UI/CLAUDE.md`), tray `Views/TrayIcon`, pages in `Views/Pages/` (Home, Overlay, About, then the optional Hotkeys and Sounds, and first-run Setup), `OverlayWindow` + `Controls/OverlayBadge`, `TrayIcon` (right-click menu), one `MainViewModel` split into partial files per page, theme in `Themes/`, composition in `App.axaml.cs` | Talk to the OS only through the `MicControl.Platform` interfaces |
| `MicControl.Windows` | Core Audio via NAudio (only `NAudio.Core` and `NAudio.Wasapi`: the all-in-one `NAudio` package pulls in WinForms and WPF, 11.5 MB of the exe), low-level keyboard/mouse hooks (`GlobalHotkeyService`, `VirtualKeys`), WASAPI playback, autostart (Run key or scheduled task), overlay click-through, `WindowsInstaller` | Windows-only code goes here |
| `MicControl` | Windows exe (`MicControl.exe`, also the setup): `Program.cs` (app or setup mode, single instance, show/exit signals, DI registration of Windows services), `UpdateService` (GitHub Releases), `app.manifest` | |
| `MicControl.Tests` | xUnit v3: core tests plus headless Avalonia UI tests (`Ui/`, with fakes in `Ui/Fakes.cs`) | |

## Conventions
- Package versions live only in `Sources/Directory.Packages.props`; shared build settings are in `Directory.Build.props`, style in `.editorconfig`.
- XAML uses compiled bindings (`x:DataType`). For a ComboBox, use `DisplayMemberBinding` and `SelectedValueBinding` with `x:DataType`. Selected values must never be `null`: use `""` for "None" or "Default". Setters bound to `SelectedValue` must ignore `null` (a ComboBox pushes it while it builds), or opening a page rewrites settings.
- Theme colors are our own light/dark `ThemeDictionaries` in `Themes/Theme.axaml`, so Windows 10 and 11 look identical. Icons use the `IconFont` resource, the bundled `MicControl.UI/Assets/Fonts/MicControlIcons.ttf` at the Segoe Fluent Icons code points the XAML writes (`&#xE720;` is the microphone), so every OS shows the same icons; to add one, see the `generated-assets` skill.
- Settings: `%APPDATA%\MicControl\settings.json` (camelCase, hotkeys stored as text like `"Ctrl+F1"`, compatible with MicSwitch 1.x); logs in `%APPDATA%\MicControl\logs\MicControl-yyyyMMdd.log` (daily, last 7 kept, shared by every process under a named mutex). A hand-edited or damaged `settings.json` never crashes the app or gets saved over (`AppSettings.Normalize`, `SettingsStore.LoadError`; details in the `product-behavior` skill). The format starts at version 1 (`AppSettings.CurrentVersion`); bump it only with a migration. Keep the MicSwitch 1.x import working (`LegacyConfigMigrator`, `%APPDATA%\MicSwitch\release\config.cfg`).
- Every new platform capability gets an interface in `MicControl.Core/Platform`, a Windows implementation, a fake in the tests, and a flag in `PlatformCapabilities` if some platforms can't support it.
- Add or extend tests for behavior changes; UI behavior can be tested headlessly with `[AvaloniaFact]`.
- Logging: `[LoggerMessage]` source-generated methods only (the analyzers reject `LogInformation(...)` and costly arguments). The file's level is the `logLevel` setting (About → Log detail, default Debug), applied live through `LogLevelSwitch`. Information for user actions and state changes, Debug for decisions and internals, Trace for high-frequency events. Log what the user did and why the app did something (e.g. which monitor the overlay chose), so a test pass can be followed in the log.

## Platform scope
- **Windows 10 (2004+) and Windows 11 are the only supported platforms right now.** Windows 11 has only been tested in a VM.
- **Linux comes after the Windows audio engine work (see `ROADMAP.md`): KDE Plasma 6/Wayland** (no X11 support), first target **CachyOS** and the AUR (Arch-based). Plan: GlobalShortcuts portal for hotkeys, PipeWire for audio, layer-shell overlay, XDG autostart. Prototype hotkeys (portal shortcuts need user approval; true hold-to-talk is the open question) and the layer-shell overlay first: they decide what the Linux version can do.
- **Packaging is native per OS, never Flatpak/Snap/AppImage** (developer's design choice):
  - Windows: the app installs itself (see "Installer and updates") plus the in-app updater, which only installs releases signed with the developer's key (see "Signing").
  - Linux: AUR `miccontrol`/`miccontrol-bin` (framework-dependent, system-wide), later a `.deb`. On Linux the `IUpdateService` reports "managed by your package manager" and the About page says so: no in-app updates there.
  - macOS (next year): no paid Apple developer account, so no notarization; a Homebrew tap building from source plus a `.dmg`.
  - Details of the Linux and macOS packaging plans: the `roadmap` skill (readable version: `ROADMAP.md`).
- The developer works with **Visual Studio Enterprise** and **Sublime Text** only; never suggest VS Code or JetBrains tooling.

## Naming and MicSwitch compatibility
- **Name**: "MicControl" everywhere (UI, tray, notices via `MainViewModel.AppName`; repo `ChaoticHolon/MicControl`, solution, projects, namespaces, exe, settings folder, mutex, autostart entries, installer, packages). No "NG" or "Next Generation": those pre-1.0 names are gone, and nobody runs those builds, so there is no migration from them.
- Keep "MicSwitch" only for compatibility: importing `%APPDATA%\MicSwitch` (`SettingsStore` legacy folder, `LegacyConfigMigrator` 1.x type names), taking over old autostart entries (only those whose MicSwitch exe is gone; an installed MicSwitch keeps its own), and the credit "Inspired by MicSwitch by Xab3r" (About page, README).
