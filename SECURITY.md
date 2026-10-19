# Security policy

## Reporting a vulnerability
Please report security problems **privately**, through GitHub: [Report a vulnerability](https://github.com/ChaoticHolon/MicControl/security/advisories/new) (or the Security tab → Report a vulnerability). Don't open a public issue or pull request for them, so a fix can ship before the details are public.

Helpful to include:
- the MicControl version (About page) and your Windows version,
- what an attacker could do, and what they need first (for example, a standard account on the same PC),
- steps to reproduce, or a proof of concept.

MicControl is maintained by one developer in their spare time. Reports are read and answered as soon as possible; you'll hear back when the report is confirmed and again when a fixed release is out, and you're credited in the advisory unless you'd rather not be.

## Supported versions
Only the latest release gets security fixes. The app updates itself (About → Check for updates), so staying current is one click.

## What's in scope
Especially welcome are problems with:
- **Updates**: the updater only installs a setup whose SHA-512 is listed in a `SHA512SUMS` signed with the release key (Ed25519, fingerprint `SHA256:kbTiCgbf4Xowvw+brzv9QcWBEF9uY/poFby0XrMbDDo`, listed in [`docs/release_signers`](docs/release_signers)). A way to make it run anything else is a serious bug.
- **The installer and elevation**: the all-users install, the administrator prompt, and the scheduled task used for "Start with Windows".
- **The global keyboard and mouse hook**: MicControl watches for its hotkeys only; anything that lets it leak or be abused to read other input matters.
- **Local files**: settings, logs and imported MicSwitch configurations.

Out of scope: attacks that already need administrator rights or full control of the user's account, and problems in third-party components (please report those to their projects; tell us too if MicControl ships an affected version).

## Verifying a download
Each release's `SHA512SUMS` is signed (`SHA512SUMS.sig`). See [Verifying a download](README.md#verifying-a-download) for the two commands that check a setup before you run it.
