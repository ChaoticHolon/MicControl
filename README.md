# MicControl

**MicControl**: mute and unmute your microphone system-wide with custom hotkeys, with an always-on-top overlay, sounds and a tray icon. It works with every app that uses the microphone: Discord, Teams, Zoom, games and more.

MicControl is the successor to and inspired by [MicSwitch](https://github.com/iXab3r/MicSwitch) by Xab3r, but it's built from the ground up with .NET 10 and Avalonia, with explicit plans to eventually release it cross-platform as Xab3r intended, but never got around to. MicSwitch 1.x settings are imported automatically.

## Screenshots
| Light | Dark |
| --- | --- |
| ![Home, light](docs/screenshots/home-light.png) | ![Home, dark](docs/screenshots/home-dark.png) |
| ![Hotkeys, light](docs/screenshots/hotkeys-light.png) | ![Overlay settings, dark](docs/screenshots/overlay-dark.png) |

| The overlay | Setup |
| --- | --- |
| ![Overlay, live](docs/screenshots/overlay-live.png) ![Overlay, muted](docs/screenshots/overlay-muted.png) | ![Setup](docs/screenshots/installer-light.png) |

## Features
- **Three modes:** push-to-talk (the default), toggle, and push-to-mute
- **Hotkeys that work everywhere,** including in elevated games: keyboard keys, combinations and mouse side buttons, with an optional alternative key and an "exclusive" option that hides the key from other apps
- **Custom hotkeys** (optional, under About → Advanced, then on the Hotkeys page) for mute, unmute, toggle, push-to-talk, speaker mute and volume, and showing or hiding the overlay
- **One microphone or all of them** at once, handy for streamers with several inputs
- **Overlay:** a small always-on-top badge that shows Live or Muted, even over games. Snap it to any corner of any monitor or drag it anywhere; show it always, only when muted or only when live; adjust size and opacity; optional speaking indicator; your own icons
- **Sounds** when the microphone switches on and off: use the built-in cues, or (with Custom sounds under About → Advanced) your own audio files, volume and playback device
- **Lives in the tray:** starts with Windows, stays out of the way, a click on the tray icon opens the window, and right-click has quick controls
- **Light and dark,** following Windows and its accent colour
- **Installs for everyone or just you** (no admin needed for just you), and **updates itself** from GitHub Releases, verified before it runs
- **MicSwitch 1.x settings are imported** automatically on first start

## Roadmap
Next up: a 1.0.1 that tests the updater, then device aliases and other 1.x features, an audio engine with voice processing and noise removal, per-app capture, Linux (KDE Plasma on Wayland), and output buses for streaming. macOS follows next year. See [ROADMAP.md](ROADMAP.md) for the full plans.

## Install
Download `MicControl-Setup-<version>.exe` from [Releases](https://github.com/ChaoticHolon/MicControl/releases) and choose **Everyone on this PC** (Program Files, needs admin) or **Just me** (your own Programs folder, no admin). Settings stay in your own `%APPDATA%\MicControl` either way. Windows SmartScreen may warn because the setup isn't code-signed (yet): choose **More info → Run anyway**. For unattended installs: `MicControl-Setup-<version>.exe --install --silent --scope=all` (or `--scope=user`); uninstall with `MicControl.exe --uninstall --silent`.

### Using it with Discord, Zoom and other voice apps
MicControl mutes the microphone itself, for every app at once, so let it be the only thing that switches your microphone. In each voice app, use voice activity or an open microphone instead of that app's own push-to-talk, and turn off the app's "automatically adjust microphone volume" if you keep the input volume in MicControl. If you do want an app's own push-to-talk on the same key, leave **Exclusive** off: an exclusive key is hidden from every other app.

## How to build

Requirements: Windows 10 (version 2004 or later) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (included with Visual Studio 2026's ".NET desktop development" workload).

```
git clone https://github.com/ChaoticHolon/MicControl.git
cd MicControl/Sources
dotnet build MicControl.slnx
dotnet test --project MicControl.Tests
dotnet run --project MicControl
```

In Visual Studio, open `Sources\MicControl.slnx`, set **MicControl** as the startup project and run. Start Visual Studio as administrator: the app requests elevation so hotkeys keep working in elevated games.

To build the setup, run `installer\build.ps1` from the repository root. It writes `artifacts\MicControl-Setup-<version>.exe`, which is the app itself published as one file: started under that name it shows the installer.

Releases: pushing a `vMAJOR.MINOR.PATCH` tag (e.g. `v1.1.0`) on a commit in `master` builds the setup in GitHub Actions as a draft release with a `SHA512SUMS` file. The tag must match `<Version>` in `Sources\Directory.Build.props`. The developer then signs `SHA512SUMS` with the release key, which never leaves their PC, and publishes the release (`tools\Signing.ps1 SignRelease v1.1.0`). The in-app updater only installs a setup whose SHA-512 is in a `SHA512SUMS` carrying that signature.

### Verifying a download
Every release has `SHA512SUMS` and its Ed25519 signature `SHA512SUMS.sig`, made with `ssh-keygen`. The trusted keys are in [`docs/release_signers`](docs/release_signers). With OpenSSH (built into Windows 10 and 11, macOS and Linux), in the folder with the downloads:

```
ssh-keygen -Y verify -f release_signers -I release@miccontrol -n miccontrol-release -s SHA512SUMS.sig < SHA512SUMS
sha512sum -c SHA512SUMS --ignore-missing
```

On Windows without `sha512sum`, compare `(Get-FileHash MicControl-Setup-1.0.0.exe -Algorithm SHA512).Hash` with the line in `SHA512SUMS`. Commits and release tags are signed too, so GitHub shows them as Verified.

The UI is built with [Avalonia](https://avaloniaui.net) so it can run on Linux soon (KDE Plasma 6 on Wayland is the planned first target). The tests, including headless UI tests, run on Windows and Linux.

### Telemetry
The build tools phone home by default; this repository turns that off where it can. Avalonia's build telemetry is disabled for every build in `Sources\Directory.Build.targets`, and CI also opts out of the .NET SDK's and the test platform's. Those two only listen to environment variables, so set them once on your own PC (a new terminal and Visual Studio pick them up):

```powershell
foreach ($v in 'DOTNET_CLI_TELEMETRY_OPTOUT', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'AVALONIA_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO') { [Environment]::SetEnvironmentVariable($v, '1', 'User') }
```

### Project layout
| Project | Contents |
| --- | --- |
| `MicControl.Core` | Platform-independent logic and contracts: settings model and JSON storage, MicSwitch 1.x import, hotkey text format, mute rules, notification sound library, update feed and release signature checks (Ed25519, SSHSIG), and the `MicControl.Platform` interfaces each OS implements |
| `MicControl.UI` | Avalonia 12 UI (Fluent theme, light/dark): views, view models (CommunityToolkit.Mvvm), tray icon, app composition |
| `MicControl.Windows` | Windows implementations: Core Audio mute/volume (NAudio), low-level keyboard/mouse hooks, WASAPI sound playback, start with Windows, overlay click-through |
| `MicControl` | Windows executable (`MicControl.exe`): wires the UI to the Windows services; single instance; updates through the installer |
| `MicControl.Tests` | xUnit v3 tests: core logic plus headless UI tests against fake platform services |

### Settings
Settings are stored in `%APPDATA%\MicControl\settings.json`, logs in `%APPDATA%\MicControl\logs`. On first start, MicControl imports a MicSwitch 1.x `%APPDATA%\MicSwitch\release\config.cfg`, with its custom icon and sounds (MicSwitch's beeps become MicControl's own cues). The mode and startup state start from MicControl's defaults (push-to-talk, muted), and setup still runs so you can review what was imported. A MicSwitch autostart entry is taken over by MicControl only when the MicSwitch program it starts has been removed; an installed MicSwitch keeps its own.

## Security
Found a security problem? Please report it privately, as described in [SECURITY.md](SECURITY.md).

## Privacy
MicControl collects no telemetry and sends nothing about you or your PC. The only network access is the update check, which reads the latest release from GitHub (About → Check automatically at startup turns it off) and downloads an update when you install one.

## License
MicControl is free software: you can redistribute it and/or modify it under the terms of the [GNU General Public License](LICENSE), version 3 or (at your option) any later version. It comes with no warranty.

It bundles open-source components under their own licenses (MIT, BSD and others), listed with their full texts in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt). The setup installs both files next to `MicControl.exe`, and About → License opens them. After changing packages, regenerate the notices with `dotnet run tools/GenerateThirdPartyNotices.cs` from the repository root; CI fails while they're out of date.

## Credits

Inspired by [MicSwitch](https://github.com/iXab3r/MicSwitch) by Xab3r. Icons from Microsoft's [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT licence, bundled in `Sources/MicControl.UI/Assets/Fonts`).
