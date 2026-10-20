---
name: generated-assets
description: How MicControl's shipped assets are generated - the app and tray icons (tools/GenerateAppIcons.cs), the Relay on/off cue sounds (tools/GenerateCueSounds.cs) and the bundled icon font (tools/GenerateIconFont.py). Load before changing an icon, a cue sound, adding an icon glyph, or touching Assets/.
---

# Generated assets

Hard rule (CLAUDE.md): these are generated, so change the generator and run it again, never edit the images, WAVs or font by hand. Run each from the repo root and commit what it writes.

## App icon
- App icon: the Home page's status badge, a green (`#0F7B0F`, live) or red (`#C42B1C`, muted, with a slash) circle with a white microphone outline; tray sizes (32 px and below) use a solid capsule so they stay readable. Drawn from simple shapes by `tools/GenerateAppIcons.cs` (`dotnet run tools/GenerateAppIcons.cs` from the repo root; pass a folder to also write a preview sheet), never a font glyph, so it's ours to ship. It writes `MicControl.UI/Assets/microphoneEnabled|Disabled.ico` (16-256 px: window, tray, and the live one is the exe's icon) and `.png` (256 px: sidebar). Change it by editing the generator, never the images. The window and tray use the `.ico` (`MainViewModel.CurrentWindowIcon`) unless the user picked their own overlay icons. A picked icon is checked by its header first (`Imaging/ImageSize`: over 64 megapixels, or a size of 0, is refused before Skia decodes it), and an unreadable one falls back to the built-in icon.

## Cue sounds
- Notification sounds must be CC0/public-domain.
- Default cues are "Relay on" / "Relay off", synthesised (so ours to ship) by `tools/GenerateCueSounds.cs` (`dotnet run tools/GenerateCueSounds.cs` from the repo root): the "asymmetric relay", a dark 2.2 kHz latch click over a faint steady 1175 Hz triangle for unmute, and a muffled 450 Hz release thud over a faint 494 Hz sine for mute. Both are short (~40/55 ms), fade in instantly, and stop clean with no tail; change them by editing the generator, never by hand-editing the WAVs. MicSwitch's beeps (Beep300/Beep750) don't ship: a 1.x import that used one gets the relay cue for that slot.

## Icon font
- Theme colors are our own light/dark `ThemeDictionaries` in `Themes/Theme.axaml`, so Windows 10 and 11 look identical. Icons use the `IconFont` resource: the bundled `MicControl.UI/Assets/Fonts/MicControlIcons.ttf` ("MicControl Icons"), Microsoft's Fluent UI System Icons (MIT, licence beside the font) subset to the icons we use and placed at the Segoe Fluent Icons code points the XAML writes (`&#xE720;` is the microphone), so every OS shows the same icons. `tools/GenerateIconFont.py` builds it (`pip install fonttools`, then `python tools/GenerateIconFont.py` from the repo root): to add an icon, add its Segoe code point and Fluent name to `ICONS` and run it again; never edit the font by hand.
