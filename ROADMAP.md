# MicControl roadmap

MicControl 1.0 mutes and unmutes your microphone system-wide. From here it grows into voice processing and mixing for calls and streaming. Everything below is planned, not built, and the order can still change.

The full technical notes behind each item are in `.claude/skills/roadmap/SKILL.md`.

## Order

1. **1.0.1**: a small release that tests the signed in-app updater end to end.
2. **1.x features**: device aliases and the other smaller features listed below.
3. **Audio engine**: MicControl's own audio pipeline.
4. **Speech enhancement**: noise removal and echo cancellation on top of the engine.
5. **Per-app capture**: picking up the audio of single apps.
6. **Linux**: KDE Plasma 6 on Wayland, first on CachyOS, carrying the engine over to PipeWire.
7. **Output buses**: separate mixes for monitoring, streaming and recording.

Everything up to per-app capture is Windows work and comes before Linux. Only the output buses come after it. macOS is planned for next year.

## Principles that hold throughout

- **Mute never depends on the engine.** If the engine fails or a route breaks, the app falls back to the Core Audio endpoint mute, and push-to-talk fails muted.
- **No bundled virtual audio driver.** Users who want routing install a virtual cable themselves (see "Routing to other apps").
- **Nothing for Microsoft proprietary software.** No Teams integration or plugins.
- **Everything stays local.** Control interfaces listen on this machine (or the LAN where stated), and the app still sends nothing anywhere.

## 1.x features (before the engine)

- **Device aliases**: your own names for devices, keyed by device id in settings. Small, so it could ship early in 1.x.
- **Settings export and import.**
- **Mic diagnostics page**: spots a privacy setting blocking the mic, exclusive mode held by another app, clipping, a hardware mute switch, and the sample rate. Once the engine exists it also shows latency, CPU use and dropouts.
- **Hotkey conflict warning.**
- **Beta update channel**: opt-in, signed pre-releases.
- **Per-app profiles**: mode, hotkey and microphone follow the app in the foreground, with a game mode for full-screen games.
- **Fallback microphones**: the next mic takes over when one is unplugged and hands back when it returns. Also an output switcher.
- **Default and communications device sync.**
- **Game controller buttons (XInput) as hotkeys.**
- **Command-line control**: `--toggle`, `--mute` and so on.
- **Push-to-talk refinements**: a release tail, double-tap to latch, and timed mute.
- **Overlay level ring**: opt-in, and it also warns when you talk while muted.
- **"Mic in use" awareness**: shows which apps are listening, and can show the overlay only while the mic is in use.
- **Translations and an accessibility pass.**
- **"On air" light** over local MQTT or Home Assistant: off by default and LAN only, and README's Privacy section must mention it.

## Local control API and plugins

One local-only interface (a named pipe or localhost, with OSC) that MIDI, the command line and every plugin use. Each plugin lives in its own repository:

- OBS
- Discord (Vencord/BetterDiscord): control only, such as mute sync and input selection
- Stream Deck, Bitfocus Companion, Touch Portal, Loupedeck
- Mumble, TeamSpeak
- A KDE Plasma widget

## Audio engine

- A separate `MicControl.Audio` project behind the `MicControl.Platform` interfaces.
- **Devices**: WASAPI and ASIO from the start. ASIO is wanted soon: Steinberg's ASIO SDK can be licensed under GPLv3 since Oct 2025, and `NAudio.Asio` is MIT. Many ASIO drivers accept only one client, and ASIO bypasses the Windows mixer, so on ASIO the mute happens inside the engine.
- **Voice chain**: gate, compressor, limiter, EQ and auto level, with shareable presets ("Broadcast", "Podcast", "Clean"), and VST3 effect hosting (the VST3 SDK is MIT since 3.8).
- **Also**: sidetone, ducking, and a "how do I sound" record-and-playback test.
- **Design**: 10 ms blocks (480 samples at 48 kHz) through preallocated ring buffers. The audio thread never allocates and runs at MMCSS "Pro Audio" priority.
- **Libraries**: keep NAudio (MIT) for devices. For a cross-platform engine, consider miniaudio (public domain/MIT-0, through P/Invoke). Not CSCore (abandoned, MS-PL) or BASS (proprietary).

## Speech enhancement

- GPU-first through ONNX Runtime: DirectML on Windows, CUDA/TensorRT, Core ML on macOS.
- Posted system requirements, plus a benchmark on your own machine before it can be turned on. If your machine can't keep up, it stays off and says why.
- **Models**: first prototype ClearerVoice-Studio's models (MossFormer2) streaming in a sliding window, measuring latency (target 20-40 ms look-ahead) and quality. ClearerVoice also suits cleaning saved clips. DeepFilterNet (unmaintained) and RNNoise (sounds robotic) are ruled out.
- **Echo cancellation**: WebRTC AEC3.
- **Fallback**: Windows 11's Voice Clarity is a possible light option.
- Every model's licence gets checked. For example, Meta's denoiser weights are CC-BY-NC, so they can't ship.

## Per-app capture

Capture single apps (Discord, Spotify…) by process:

- **Windows**: WASAPI process loopback (`ActivateAudioInterfaceAsync` with `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, Windows 10 2004+, which is already the minimum), likely through our own interop feeding NAudio.
- **macOS**: ScreenCaptureKit (13+) or Core Audio process taps (14.4+).
- **Linux**: PipeWire natively.

## Routing to other apps

- **No bundled driver.** Every Windows virtual audio device is a kernel driver. Our own would need an EV certificate and Microsoft attestation signing, a yearly cost, and the SysVAD-derived drivers (such as VirtualDrivers/Virtual-Audio-Driver) carry Microsoft's MS-PL sample code.
- **Bring your own cable.** Users who want routing install a virtual cable themselves (VB-Audio Virtual Cable, Virtual-Audio-Driver…). The processed mic and the buses route into it, and the feature turns on only when a cable is detected; otherwise it says why it's off. Detection is generic, not a fixed list of products.
- **OBS** gets its own GPL plugin that only carries audio into OBS. Control goes through OBS's local obs-websocket.
- **Linux** needs no cable: PipeWire lets the app create its own virtual devices.
- **Setup wizard**: detects the cable and OBS, sets up the route and runs the sound test.

## Routing levels (output buses)

One setting, from simplest to most powerful:

| Level | What it does |
| --- | --- |
| Off (default) | Mute only, no engine |
| Simple | One output: the processed mic into the virtual cable |
| Streamer | Three buses: Monitor, Stream and Record |
| Advanced | Any number of inputs and buses, set up in a panel that's normally hidden |

- The engine and the settings model N buses from the start.
- **Inputs**: hardware (WASAPI/ASIO), a virtual cable set as Windows' default output (for system audio), single apps, app groups, and later network audio (VBAN-compatible).
- **UI**: a matrix with colour-coded routes, a mute per bus ("stream mute"), and a panic button that mutes every bus.

## Streamer support

- Detect OBS and Streamlabs (by process, then through obs-websocket for live/recording state), and offer the Streamer level once. It never switches silently.
- **OBS actions** become targets in the existing action system (custom hotkeys, profiles, MIDI, the control API, the panic button): switch scene, start/stop stream or recording, save replay, toggle a source, add markers. No automation engine of our own: Streamer.bot and others already do that.
- **Scene-aware mute**: for example, a "BRB" scene mutes the Stream bus.
- **Stream-safe music**: chosen apps play on Monitor but stay off Stream while you're live.
- **One-click OBS setup.**
- **Clips** only through OBS/Streamlabs (replay buffer), never a recorder of our own.

## Linux

- **Desktop**: KDE Plasma 6 on Wayland (no X11), first target CachyOS.
- **Plan**: the GlobalShortcuts portal for hotkeys, PipeWire for audio, a layer-shell overlay, XDG autostart. Hotkeys (portal shortcuts need the user's approval, and true hold-to-talk is the open question) and the layer-shell overlay get prototyped first, because they decide what the Linux version can do.
- **Packages**: native only, never Flatpak, Snap or AppImage.
  - Two AUR `PKGBUILD`s: `miccontrol` builds from the source tag with `dotnet publish`; `miccontrol-bin` repackages the release's `miccontrol-<version>-linux-x64.tar.gz`. Both depend on Arch's `dotnet-runtime` and install system-wide (`/usr/lib/miccontrol`, a `/usr/bin/miccontrol` launcher, a desktop file in `/usr/share/applications`, hicolor icons). Settings go in `~/.config/MicControl`.
  - Updates come from pacman, shelly, paru or yay; the app says "managed by your package manager" and has no in-app updates there. A pacman repository of our own comes later.
  - A `.deb` for Debian/Ubuntu later, built in CI with `nfpm` (one config can also emit an Arch package).
- **Releases** then ship the Windows setup, a Linux tarball and the signed `SHA512SUMS`, followed by an AUR `pkgver` and checksum bump.
- **Timeline**: after the Windows audio engine, enhancement and per-app capture, so it may slip past this year.

## macOS (next year)

- **App**: an Avalonia head project, CoreAudio mute, CGEventTap hotkeys (which need the Input Monitoring/Accessibility permission), and a floating click-through overlay window.
- **No notarization**: the developer won't pay Apple for a developer account. Instead:
  - A **Homebrew tap** (a GitHub repo) whose formula builds from source with `dotnet publish`. Locally built apps aren't quarantined, so Gatekeeper doesn't block them, and `brew upgrade` handles updates. Not the official homebrew-cask, which is phasing out casks that fail Gatekeeper. Homebrew installs report "managed by Homebrew".
  - A **`.dmg`** on GitHub Releases for everyone else, documenting the one-time System Settings → Privacy & Security → "Open Anyway" step. In-app updates for it go through NetSparkleUpdater behind `IUpdateService` (files the app downloads itself aren't quarantined).
  - **Signing** with a self-made certificate kept in CI secrets (not Apple-issued), so the Input Monitoring/Accessibility grants survive updates. Ad-hoc signing (`codesign -s -`) would satisfy Apple Silicon but loses the grants on every update.

## Backlog

- Mute on lock or sleep (Toggle mode)
- Portable mode
- OpenRGB lighting (GPL-2, low priority)
- Local live captions (Whisper through ONNX Runtime, offline, also as an OBS caption source)
- A soundboard (clips into the Stream bus or the cable)
- macOS audio core (Core Audio HAL, ScreenCaptureKit)
- Network audio (VBAN-compatible)
