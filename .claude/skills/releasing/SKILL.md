---
name: releasing
description: How a MicControl release is cut, signed and published, and how the release, commit and cloud signing keys work (tools/Signing.ps1, SignRelease, GitSigning, CloudKey, key rotation, SecretBox). Load before preparing a release, touching release.yml, signing, ReleaseKeys/release_signers, or the Security/ crypto code.
---

# Releasing and signing MicControl

The hard rules (signed tags only, tags never moved or deleted once released, never script `gh release edit`, release key never in CI) are in CLAUDE.md; this is the procedure behind them.

## Releasing (all from the developer's PC, except the draft)
1. Bump `<Version>` in `Sources/Directory.Build.props` on `dev`, then condense the history (see "Commits" in CLAUDE.md).
2. The developer pushes `dev` and `master` (the same commit) from their PC.
3. Tag it, signed: `git tag -s vX.Y.Z -m "MicControl X.Y.Z"; git push origin vX.Y.Z`.
4. `release.yml` checks that the tag is exactly `vMAJOR.MINOR.PATCH`, that the commit is in `master`, that `<Version>` matches and that no release exists for the tag yet, then builds and tests in Release and creates a **draft** release with `MicControl-Setup-<version>.exe`, `SHA512SUMS` and generated notes.
5. `powershell -ExecutionPolicy Bypass -File tools\Signing.ps1 SignRelease vX.Y.Z` signs and publishes it.
6. The developer replaces the notes in GitHub's web UI, from `docs/release-notes-template.md`. Never script `gh release edit`: it hung on the developer's PC.
- A failed check publishes nothing: fix it, then the developer deletes the tag from their PC (`git push origin :refs/tags/vX.Y.Z`, which needs their admin bypass of the `release tags` ruleset) and tags again. A published release's tag is never deleted or moved.

## Signing keys
- **Release key** (`~/.miccontrol/release_signing_ed25519`, generated passphrase pasted at every `SignRelease`, on the developer's PC only, never in CI): `SignRelease` downloads the draft, checks every file against `SHA512SUMS`, checks that the tag's `docs/release_signers` trusts the key, signs (`ssh-keygen -Y sign -n miccontrol-release`), verifies with OpenSSH, uploads `SHA512SUMS.sig` and publishes. The public keys live twice, kept equal by a test: `ReleaseKeys.Trusted` (built into the app) and `docs/release_signers` (allowed-signers format, for `ssh-keygen -Y verify` by users, the script and future AUR packages). Rotation: ship a version trusting old and new key, sign later releases with the new one, drop the old a release later. Losing the key means installed copies can't update in-app until a new version is installed by hand, so the developer keeps an offline backup.
- **Commit signing** (GitHub "Verified"): `GitSigning` sets up the developer's PC (`~/.ssh/github_signing_ed25519`, its generated passphrase pasted once into `ssh-add`, then kept by the Windows SSH agent, `gpg.format ssh`, `commit.gpgsign`/`tag.gpgsign`, Windows' own `ssh-keygen` as `gpg.ssh.program` so Visual Studio and Git for Windows use the agent). Cloud sessions sign with a separate, revocable key (`CloudKey`): the environment variable `MICCONTROL_GIT_SIGNING_KEY` (base64 private key) is turned into git config by the SessionStart hook `.claude/hooks/git-signing.sh`, which does nothing without the variable or `ssh-keygen`. Both public keys are registered on GitHub as Signing Keys. The cloud environment also signs commits by itself (`gpg.ssh.program` is the platform's `/tmp/code-sign`, with a key that isn't on the developer's account); with the variable set, the hook points `gpg.ssh.program` back at `ssh-keygen` so the cloud key signs instead.
- `Security/SecretBox` (not used yet): authenticated encryption for local data, ChaCha20-Poly1305 where .NET has it (Windows 11, Linux, and macOS through CryptoKit, still to be confirmed on a Mac; not Windows 10, which needs build 20142), otherwise AES-256-GCM; layout algorithm byte · 12-byte nonce · ciphertext · 16-byte tag. It needs a key from somewhere safe (e.g. DPAPI on Windows, the keychain on macOS) before anything uses it; settings stay plain, hand-editable JSON.
