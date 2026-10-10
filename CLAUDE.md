# MicControl (MicControlNG)

System-wide microphone mute/unmute with hotkeys, an always-on-top overlay, sounds and a tray icon. Rework/rebuild of MicSwitch by Xab3r.
- **Display name** (UI, tray, notices): "MicControl" (`MainViewModel.AppName`); full name "MicControl Next Generation" (`ProductFullName`).
- **Technical name** (repo, solution, projects, namespaces, exe, settings folder, mutex, autostart entries): `MicControlNG`.
- **Installer and package name**: `MicControlNG` (setup wizard, Apps & Features entry, release titles, Linux/Homebrew package names). Things that launch the app (shortcuts, "Launch …" checkbox) use the app name MicControl.
- Keep "MicSwitch" only for compatibility: importing `%APPDATA%\MicSwitch` (`SettingsStore` legacy folder, `LegacyConfigMigrator` 1.x type names), taking over old autostart entries (only those whose MicSwitch exe is gone; an installed MicSwitch keeps its own), and the credit "Based on MicSwitch by Xab3r".
.NET 10, Avalonia 12.1 UI, C# latest. Solution: `Sources/MicControlNG.slnx`.

## Platform scope
- **Windows 10 (2004+) and Windows 11 are the only supported platforms right now.** Windows 11 has only been tested in a VM.
- **Linux is next: KDE Plasma 6/Wayland** (no X11 support), first target **CachyOS** and the AUR (Arch-based). Plan: GlobalShortcuts portal for hotkeys, PipeWire for audio, layer-shell overlay, XDG autostart. Prototype hotkeys (portal shortcuts need user approval; true hold-to-talk is the open question) and the layer-shell overlay first: they decide what the Linux version can do.
- **Packaging is native per OS, never Flatpak/Snap/AppImage** (developer's design choice):
  - Windows: the app installs itself (see "Installer and updates") plus the in-app updater. Next: sign release files with an owner-held key and verify that signature in the updater, not just GitHub's SHA-256.
  - Arch/CachyOS: two AUR `PKGBUILD`s. `miccontrolng` builds from the source tag with `dotnet publish`; `miccontrolng-bin` repackages the release's `miccontrolng-<version>-linux-x64.tar.gz`. Framework-dependent on Arch's `dotnet-runtime`, installed system-wide (`/usr/lib/miccontrolng`, `/usr/bin/miccontrolng` launcher, `/usr/share/applications` desktop file, hicolor icons); settings in `~/.config/MicControlNG`. Updates come from pacman/shelly/paru/yay. Create and maintain pacman repo later.
  - Debian/Ubuntu: a `.deb` later, built in CI with `nfpm` (one config can also emit an Arch package).
  - Release tag outputs: Windows setup.exe, Linux tarball, signed `SHA256SUMS`; then bump the AUR `pkgver` and checksums.
  - On Linux the `IUpdateService` reports "managed by your package manager" and the About page says so: no in-app updates there.
- Timeline: Linux (CachyOS) release targeted this year, macOS next year.
- macOS plan: Avalonia head project, CoreAudio mute, CGEventTap hotkeys (needs Input Monitoring/Accessibility permission), floating click-through overlay window.
- **Dev's decision: no money to Apple for a developer account, so no notarization. Will likely be distributed as:
  - A Homebrew tap (GitHub repo) whose formula builds from source with `dotnet publish`: locally built apps aren't quarantined, so Gatekeeper doesn't block them, and `brew upgrade` handles updates. Not the official homebrew-cask, which is phasing out casks that fail Gatekeeper.
  - A `.dmg` on GitHub Releases for non-Homebrew users, documenting the one-time System Settings → Privacy & Security → "Open Anyway" step.
  - Sign with a self-made certificate kept in CI secrets (not Apple-issued): a stable identity keeps the Input Monitoring/Accessibility grants across updates. Ad-hoc signing (`codesign -s -`) would satisfy Apple Silicon but loses the grants on every update.
  - In-app updates for the `.dmg` via NetSparkleUpdater behind `IUpdateService` (files the app downloads itself aren't quarantined); Homebrew installs report "managed by Homebrew".
- The developer works with **Visual Studio Enterprise** and **Sublime Text** only; never suggest VS Code or JetBrains tooling.

## Commands (run from `Sources/`)
- Build: `dotnet build MicControlNG.slnx`. Warnings are errors, and the analyzers are strict (`AnalysisLevel=latest-recommended`).
- Test: `dotnet test --project MicControlNG.Tests` (Microsoft Testing Platform, opted into by the repo-root `global.json`, so it works from any folder; the old `dotnet test <proj>` form fails).
- Run (Windows): `dotnet run --project MicControlNG`. The app asks for elevation (`highestAvailable`), so run from an elevated terminal or Visual Studio started as administrator.
- Tests already run on both Windows and Linux. The Windows projects also build on Linux (`EnableWindowsTargeting`).
- Screenshots for the README and releases: set `MICCONTROL_SCREENSHOTS=1` and run the tests; `ScreenshotTests` renders the real windows (headless Skia, sample data, light and dark, greyscale text) into `docs/screenshots`. Re-run after visible UI changes and commit the images.
- Setup (from the repo root): `installer\build.ps1 [-Version 2.1.0]` publishes the app as one self-contained file and writes `artifacts\MicControlNG-Setup-<version>.exe`. CI builds it on every push; pushing a `v*` tag publishes it as a GitHub release.

## Installer and updates
- **No installer framework** (no Velopack, Inno Setup, WiX, MSIX): the developer requires the installer to look like the app, with its theme, light/dark while using the Windows accent colors, and only Avalonia can do that. Inno Setup was examined and dropped: its styles hard-code their own accent colours.
- **The app installs itself.** The release file `MicControlNG-Setup-<version>.exe` is MicControlNG.exe published single-file. `SetupCommand` (Core `Installation/`) decides per launch: a file named `MicControlNG-Setup*`, or `--install`, shows the installer; `--uninstall` (what Settings → Apps runs) shows the uninstaller; anything else runs the app. Setup mode composes only logging and `IInstaller` (it never loads or imports settings) and shows `Views/InstallerWindow` with `InstallerViewModel`, styled with the app's own theme. `--silent`, `--scope=all|user` and `--relaunch` drive unattended installs and updates.
- Two scopes (`InstallLayout`): all users (the default, owner's choice) → `Program Files\MicControlNG`, HKLM Apps entry, common Start menu, needs admin (a non-admin setup restarts itself elevated with the choice carried on the command line); just me → `%LOCALAPPDATA%\Programs\MicControlNG`, HKCU, the user's Start menu, no admin. The Apps entry is `Uninstall\MicControlNG`, named MicControlNG; the shortcut is "MicControl".
- Installed layout: `MicControlNG.exe` (single file; Avalonia's native libraries unpack to `%TEMP%\.net\MicControlNG` on first start, and the .NET host restores any missing files) plus `Resources\Notifications\*.wav`, written from resources embedded in the exe (`Sounds/…`, see MicControlNG.csproj). Each user's settings, logs and added sounds stay in their own `%APPDATA%\MicControlNG`; uninstall never touches them.
- `WindowsInstaller` (MicControlNG.Windows): closes running copies (the session exit signal, `WindowsPlatform.ExitSignalName`, asks them to exit cleanly; stragglers such as other users' copies are ended), copies itself via a staging file, writes the sounds, registers the Apps entry and creates the shortcut. Uninstall removes autostart (`StartupService.RemoveAllFor`), the shortcut and the Apps entry, then a hidden PowerShell waits for the process to exit and deletes the folder and this user's unpack cache.
- "Start with Windows" when elevated is a per-user scheduled task `\MicControlNG\<user> (<domain>)` (`StartupService.TaskName`), so users of an all-users install don't overwrite each other; non-elevated uses the user's own Run key. The pre-release shared `\MicControlNG` task is replaced or removed only if it starts this copy. Uninstall removes every task in `\MicControlNG\` that starts the uninstalled copy, then the folder if it's empty, plus the current user's Run key value.
- In-app updates (`UpdateService`, logic in Core `Updates/ReleaseFeed`): read the latest GitHub release, take `MicControlNG-Setup-<version>.exe` only if GitHub reports a SHA-256 digest for it, verify the download, run it with `--install --silent --scope=<this install's> --relaunch`, and exit (the new copy starts the app again). A copy counts as installed when it runs from one of the two install folders (`IInstaller.Current`); dev builds aren't, so they only link to the releases page.

## Layout
| Project | Contents | Rules |
| --- | --- | --- |
| `MicControlNG.Core` (net10.0) | Settings model and JSON store (`Settings/`), 1.x config import, `HotkeyGesture` text format, `MuteRules`, `NotificationSounds`, update feed parsing (`Updates/ReleaseFeed`), setup command line and install layout (`Installation/`), platform contracts (`Platform/PlatformServices.cs`) | No UI or OS dependencies |
| `MicControlNG.UI` (net10.0) | Avalonia UI: sidebar shell `Views/MainWindow`, tray `Views/TrayIcon` + `Views/TrayPopupWindow`, pages in `Views/Pages/` (Home, Extra hotkeys, Sounds, Overlay, About, first-run Setup), `OverlayWindow` + `Controls/OverlayBadge`, `TrayIcon` (right-click menu), one `MainViewModel` split into partial files per page, theme in `Themes/`, composition in `App.axaml.cs` | Talk to the OS only through the `MicControlNG.Platform` interfaces |
| `MicControlNG.Windows` | Core Audio via NAudio, low-level keyboard/mouse hooks (`GlobalHotkeyService`, `VirtualKeys`), WASAPI playback, autostart (Run key or scheduled task), overlay click-through, `WindowsInstaller` | Windows-only code goes here |
| `MicControlNG` | Windows exe (`MicControlNG.exe`, also the setup): `Program.cs` (app or setup mode, single instance, show/exit signals, DI registration of Windows services), `UpdateService` (GitHub Releases), `app.manifest` | |
| `MicControlNG.Tests` | xUnit v3: core tests plus headless Avalonia UI tests (`Ui/`, with fakes in `Ui/Fakes.cs`) | |

## Product decisions (from the owner)
- Default mode is Push-to-talk; Toggle and Push-to-mute are alternatives. "Exclusive hotkey" (suppress) is off by default.
- App mode (`window.appMode`): **Tray** (default) starts hidden, including at sign-in; Close and Minimize hide to the tray, with a once-per-session notice that has "Don't show again". **Window** is a normal app: Close exits, Minimize goes to the taskbar. First run always shows the window, for setup.
- Start with Windows is on by default: a new user's first run of an *installed* copy enables it (`ApplyFirstRunDefaults`; per user, so each user of an all-users install gets it on their own first run). Dev/portable builds never register themselves, and existing or imported users keep their choice. Setup shows the toggle, so it's easy to turn off.
- The tray icon is always shown. Left-click never changes state; it opens the quick popup (`TrayPopupWindow`: state, Mute button, level meter, microphone and mode, "Open full window"), positioned only from the screen work area (`PopupPlacement`) so KDE Plasma can reuse it. Right-click: Open · Mute/Unmute · Mode · Microphone · Overlay · Play sounds · App mode · Start with Windows · Exit.
- Extra hotkeys are a dynamic list of key → action rows; the action list hides whatever the main key already does in the current mode.
- Overlay is a translucent Discord inspired badge, positioned by corner presets or "Edit position" (drag); click-through otherwise.
- The level meter and speaking indicator open the mic (OS "in use" indicator), so the meter only runs while Home/Setup is visible and the speaking indicator is opt-in.
- Notification sounds must be CC0/public-domain.
- Default cues are "Relay on" / "Relay off", synthesised (so ours to ship) by `tools/GenerateCueSounds.cs` (`dotnet run tools/GenerateCueSounds.cs` from the repo root): the "asymmetric relay", a dark 2.2 kHz latch click over a faint steady 1175 Hz triangle for unmute, and a muffled 450 Hz release thud over a faint 494 Hz sine for mute. Both are short (~40/55 ms), fade in instantly, and stop clean with no tail; change them by editing the generator, never by hand-editing the WAVs. The 1.x beeps stay selectable. Settings older than version 5 (and 1.x imports) still on the untouched old default pair (Beep750 muted, Beep300 live) are moved to the relay cues once; any other choice is kept.

## Conventions
- Package versions live only in `Sources/Directory.Packages.props`; shared build settings are in `Directory.Build.props`, style in `.editorconfig`.
- XAML uses compiled bindings (`x:DataType`). For a ComboBox, use `DisplayMemberBinding` and `SelectedValueBinding` with `x:DataType`. Selected values must never be `null`: use `""` for "None" or "Default". Setters bound to `SelectedValue` must ignore `null` (a ComboBox pushes it while it builds), or opening a page rewrites settings.
- Theme colors are our own light/dark `ThemeDictionaries` in `Themes/Theme.axaml`, so Windows 10 and 11 look identical. Icons use the `IconFont` resource (Segoe Fluent Icons, falling back to Segoe MDL2 Assets). Linux will need a bundled icon font.
- Settings: `%APPDATA%\MicControlNG\settings.json` (camelCase, hotkeys stored as text like `"Ctrl+F1"`); logs in `%APPDATA%\MicControlNG\logs\MicControlNG-yyyyMMdd.log` (last 7 kept). Keep the MicSwitch import working (legacy folder and 1.x `LegacyConfigMigrator`), and keep hotkey strings compatible with 1.x.
- Every new platform capability gets an interface in `MicControlNG.Core/Platform`, a Windows implementation, a fake in the tests, and a flag in `PlatformCapabilities` if some platforms can't support it.
- Add or extend tests for behavior changes; UI behavior can be tested headlessly with `[AvaloniaFact]`.
- Logging: `[LoggerMessage]` source-generated methods only (the analyzers reject `LogInformation(...)` and costly arguments). The file's level is the `logLevel` setting (About → Log detail, default Debug), applied live through `LogLevelSwitch`. Information for user actions and state changes, Debug for decisions and internals, Trace for high-frequency events. Log what the user did and why the app did something (e.g. which monitor the overlay chose), so a test pass can be followed in the log.
