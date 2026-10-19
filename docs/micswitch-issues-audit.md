# MicSwitch issues audit

Every issue on [iXab3r/MicSwitch](https://github.com/iXab3r/MicSwitch/issues) (90 issues, #1 to #115, open and closed; the repository was archived on Jan 7 2024) checked against MicControl on `dev` (Oct 19 2026, after 1.0.0). Issue details were read from GitHub's issue pages; "checked" below names the code that settles each one.

## Summary

- **Carried over, fixed on `fix/micswitch-issues-audit`:** 7 (overlay losing "on top", no microphone shown as live, speaker volumes flattened and put back, Start menu after an exclusive Win hotkey, window taller than small screens, cues silent on an unplugged device, kept input volume not actually kept). Plus hardening against the device exceptions behind MicSwitch's crash reports, and a README section for voice apps.
- **Already handled by MicControl's design:** most of the crash, hotkey, startup and update bugs (table below).
- **Feature requests on the roadmap:** noted, not built.
- **Needs a check on a real PC:** the fixes that only Windows can prove (overlay z-order, Win/Alt masking), and screen reader names.

## Fixed in this branch

| MicSwitch | What went wrong there | MicControl before | Fix |
| --- | --- | --- | --- |
| #84, #88, #96, #108 (overlay "always on top" lost) | Overlay drops behind a browser, full-screen app or OBS after a while; only a restart helps | Same design: `Topmost="True"` set once on show, never re-asserted, so any newer topmost window covers it for good | `IWindowInterop.KeepOnTop`: a foreground-change WinEvent hook re-asserts `HWND_TOPMOST` (no activation, no show) |
| #114 | No microphone connected: overlay and icon show "live" | `IsMuted` is `null` with no mic; icon, tray icon and "When live" overlay all used `IsMuted == true`, so they showed the green live badge | "Not live" looks muted: `IsMutedState => IsMuted != false` drives icon, tray, status dots and overlay visibility. Test `NoMicrophone_LooksMuted_NeverLive` |
| #100 (follow-up), #101, #98 | Changing one output's volume set every output to the same level; volume "jumps back" to an old level | Speaker volume (Hotkeys page slider, volume up/down hotkeys) with "All speakers" (the default) set every output to the first one's level, and that level was then re-applied to every speaker on any device change (a headset connecting, a monitor waking) | New `IAudioEndpointGroup.ChangeVolume(delta)`: each speaker moves by the same step and nothing is kept. Test `SpeakerVolumeHotkey_StepsEachSpeaker_AndKeepsNoLevel` |
| #36 | Windows (or a voice app's automatic gain) keeps raising the mic volume | Home says "MicControl keeps your microphone at this level", but the level was only re-applied when a mic was plugged in | A kept input volume is restored whenever something else changes it. Each device's actual level after our own set is remembered, so a device with coarse volume steps can't cause a loop |
| #70, #17 | Win+key hotkeys not suppressed | Win is tracked as a modifier, but an exclusive Win+key (or Alt+key) is swallowed, so releasing Win opens the Start menu (Alt: the app's menu bar) | `GlobalHotkeyService.MaskWinAndAlt`: after swallowing the key, an unassigned key (vk E8) is sent, as AutoHotkey does |
| #56 | Main window taller than a 768 px display, can't be resized | 1000x720 by default (900 px at 125 %); a too-tall window or a saved size from a bigger monitor put the header (the only drag area) off screen | `WindowPlacement.FitToWorkingArea` on open: shrink to the monitor's working area (not below the minimum size) and move fully onto it |
| #99, #75, #104 | Sounds stop; output device lost | The chosen playback device is kept while unplugged (good), but every cue failed silently until it came back | Cues fall back to the default device while the chosen one isn't connected (logged) |
| #102, #103, #45 (crashes in device code) | `InvalidComObjectException`, `BadDeviceId` from device notifications | `AudioEndpointGroup` and `AudioDeviceService` only caught `COMException`; a driver answering E_ACCESSDENIED or E_INVALIDARG raises other types, and a crash in a device notification ends the app | Device calls now ignore `ExternalException`, `InvalidComObjectException`, `UnauthorizedAccessException`, `ArgumentException` and `InvalidCastException` |
| #109, #21 | Push-to-talk in Discord stops working; Teams and the app disagree about mute | Nothing told users to let one app own the mic | README: "Using it with Discord, Zoom and other voice apps" (voice activity in the app, Exclusive off if the app shares the key, turn off automatic mic volume) |

## Already handled

| MicSwitch | Issue | Why MicControl doesn't have it |
| --- | --- | --- |
| #105, #11 | Left click (or middle click) as hotkey blocks the mouse | A bare left or right click is refused (`GlobalHotkeyService.Register`); mouse hotkeys need a modifier for those |
| #22 | German "STRG" modifier crashes startup | Hotkeys are stored and parsed with fixed English names (`HotkeyGesture`), never the OS's localized names |
| #26, #89, #40 | Bad hotkey in config crashes every launch | An unparsable hotkey reads as "none" (`HotkeyGestureJsonConverter`); `AppSettings.Normalize` fills null sections; a failed hook install is logged, not thrown |
| #77, #76 | Media keys crash the hook / can't be used | Media keys map to virtual keys (`VirtualKeys`: MediaPlayPause etc.); the hook never throws |
| #29, #31, #30 | Hotkeys work once, alternating hotkeys miss presses, "Failed to substitute modifier" | One binding per hotkey with its own pressed state; held keys whose release never arrived are released after two checks (`ReleaseMissedKeyUps`) |
| #38 | Every keystroke logged | The hook logs nothing about keys; only our own hotkeys are logged, by name |
| #43 | Startup mute mutes every microphone | `MuteRules.InitialMute` applies to the selected group only |
| #15, #98 | Mic volume set to 0 or reset at start | Volume is only touched when "Input volume" is kept |
| #100, #28 | Selected mic lost, falls back to "All" | The choice is kept as "Disconnected device" and re-applied when it returns |
| #85 | Hotplugged mics not controlled | `AudioEndpointGroup.Rebind` on every device change, with the last mute state |
| #61, #21 | Icon out of sync after an external mute change | Endpoint volume notifications update the state on the UI thread |
| #18, #108, #115, #84 | App hangs after a while, tray menu dead | `UiWatchdog` restarts a stuck UI; `WindowGuard` protects the hidden helper windows (the 1.0 "zombie" fix) |
| #10, #83 | Window won't come back after X, or is empty | Covered by `AppModeWindowTests` and the close-to-tray rules |
| #59 | Tray icon position forgotten after updates | Installs to a fixed folder; Windows keys tray placement by exe path |
| #113, #41, #94 | Black tray icon invisible on a dark taskbar | Coloured icons (green/red) readable on both; users can pick their own |
| #44, #58 | Tray tooltip at the top left | Avalonia's tray icon, no custom tooltip window |
| #81, #69, #33 | Crash or no start without admin; startup entry broken | `highestAvailable` manifest; non-elevated autostart uses the Run key, elevated a per-user task |
| #54, #53, #39, #42, #13, #2, #57 | Updater, shortcut icon, antivirus flags | Signed self-install and verified updates; the shortcut points at the installed exe |
| #1 | Overlay on a second monitor jumps back to centre | Monitor-aware placement (`OverlayPlacement`, `OverlayPlacementTests`) |
| #97 | Several icons stacked on the overlay | One badge: the dot or the user's icon, never both (`OverlayBadge`); worth a glance on the PC |
| #95 | Cue on release, not press | The cue plays on the state change, which happens on press |
| #7, #5, #8, #3, #12, #4, #20, #27, #93, #60 | Modes, separate mute/unmute keys, mute all, custom sounds, dark theme, overlay options, hold-to-keep | Built (three modes, custom hotkey actions, All microphones, custom sounds, themes, four-stop overlay slider) |
| #87, #107, #46, #14, #6, #11 (RAM) | Reports with no details | Nothing to compare; MicControl logs and a watchdog cover the generic crash/hang cases |

## Roadmap or by design (not built)

| MicSwitch | Request | Status |
| --- | --- | --- |
| #34 | Linux | Planned (roadmap: after the audio engine work) |
| #82 | MIDI input | Planned (control API, MIDI) |
| #35, #49 | Translations | Planned (translations and accessibility pass) |
| #86 | Screen readers | Planned accessibility pass. Check with Narrator that combo boxes read names, not record types (MicSwitch read `NotificationTypeWrapperViewModel`) |
| #55 | Mic level on the overlay | Built as the opt-in speaking indicator (it opens the mic, so it stays off by default) |
| #52 | Control a subset of microphones | Not planned; one or all. Could pair with device aliases |
| #17 | Left vs right Ctrl/Alt as separate modifiers | Not built: modifiers match either side (a lone LeftCtrl key works) |
| #9 | Repeating reminder sound while muted | Not planned |
| #37 | Single click on the tray toggles mute | Dropped by the developer: click opens the window |
| #23 | Scoop package | Not planned (native packaging per OS) |
| #90 | Mute state in Discord | Planned Discord plugin |
| #78, #106, #111, #112 | "Shows muted but still transmits" | Usually another app holding its own stream or controlling the mic (#21). MicControl mutes the endpoint like MicSwitch 1.0.242 did; worth a real-device test with a USB mic that has hardware monitoring |

## Still to check on a real PC

1. Overlay set to Always: open a browser, go full screen (F11) and back, start OBS with "always on top", click the taskbar. The badge should stay on top.
2. Exclusive hotkey Win+F1 (or Alt+F1): press and release. No Start menu, no menu bar focus.
3. Unplug the only microphone: tray, overlay and window show red "No microphone", never green.
4. Custom sounds on a headset, unplug it, toggle mute: the cue plays on the default speakers.
5. Keep input volume at 50 %, then change it in Windows Sound settings: it goes back to 50 %.
