<!--
Release notes template (1.0.1 onward), following 1.0.0's layout. Copy everything below this comment into the release
in GitHub's web UI, replace X.Y.Z everywhere and fill in the intro and "What's in it".
- Screenshots: HTML <img> with width="420" for the main pair and width="280" for the smaller ones (the overlay badges
  are smaller still), never plain Markdown images, which render huge. Their raw URLs are pinned to the tag.
- Links to repository files are pinned to the tag too.
- Drop sections that don't apply (e.g. screenshots for a fix-only release).
-->
One or two sentences: what this release is about and who should update.

| Light | Dark |
| --- | --- |
| <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/home-light.png" alt="Home, light" width="420"> | <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/home-dark.png" alt="Home, dark" width="420"> |

| Custom hotkeys | The overlay | Setup |
| --- | --- | --- |
| <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/hotkeys-light.png" alt="Hotkeys" width="280"> | <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/overlay-live.png" alt="Live" width="93"> <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/overlay-muted.png" alt="Muted" width="110"> | <img src="https://raw.githubusercontent.com/ChaoticHolon/MicControl/vX.Y.Z/docs/screenshots/installer-light.png" alt="Setup" width="280"> |

## What's in it
- **New:** …
- **Fixed:** …

## Install
1. Download **MicControl-Setup-X.Y.Z.exe** below.
2. Choose **Everyone on this PC** (recommended, installs to Program Files) or **Just me** (no administrator permission needed).

Already have MicControl? It offers this update by itself (About → Updates), or run the new setup over it.

Windows SmartScreen may warn you because the setup isn't code-signed by Microsoft: choose **More info → Run anyway**. MicControl asks for administrator rights when it starts so its hotkeys keep working in games that run as administrator.

Uninstall from **Settings → Apps → MicControl**. Your settings are kept in `%APPDATA%\MicControl`.

Unattended installs: `MicControl-Setup-X.Y.Z.exe --install --silent --scope=all` (or `--scope=user`).

## Verify the download (optional)
`SHA512SUMS` lists the setup's checksum and `SHA512SUMS.sig` is its Ed25519 signature, made with the release key `SHA256:kbTiCgbf4Xowvw+brzv9QcWBEF9uY/poFby0XrMbDDo`. With [`release_signers`](https://github.com/ChaoticHolon/MicControl/blob/vX.Y.Z/docs/release_signers) in the same folder:
```
ssh-keygen -Y verify -f release_signers -I release@miccontrol -n miccontrol-release -s SHA512SUMS.sig < SHA512SUMS
sha512sum -c SHA512SUMS --ignore-missing
```

## Coming from MicSwitch?
MicControl imports your MicSwitch settings (hotkeys, microphone, sounds, overlay and window, including MicSwitch 1.x configs and custom icons) on first start, then lets you review them in setup. It starts in push-to-talk and muted, and imported custom hotkeys and sounds stay off until you choose to use them. Your MicSwitch installation is left alone, including its "start with Windows" entry, so uninstall MicSwitch once you're happy so the two don't both react to your hotkey.

## Requirements
Windows 10 version 2004 or later, or Windows 11, 64-bit only.

## License
MicControl is free software under the [GNU GPL, version 3 or later](https://github.com/ChaoticHolon/MicControl/blob/vX.Y.Z/LICENSE). Found a security problem? Please report it privately: see [SECURITY.md](https://github.com/ChaoticHolon/MicControl/blob/vX.Y.Z/SECURITY.md).
