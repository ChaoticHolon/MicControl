#!/bin/bash
# Signs this cloud session's commits with the cloud signing key (tools/Signing.ps1 CloudKey), so they show as
# Verified on GitHub. The key comes from the environment's MICCONTROL_GIT_SIGNING_KEY (base64 of the private key);
# without it, or without ssh-keygen, commits stay unsigned rather than failing.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ] || [ -z "${MICCONTROL_GIT_SIGNING_KEY:-}" ]; then
  exit 0
fi

if ! command -v ssh-keygen >/dev/null 2>&1; then
  (apt-get install -y -q openssh-client || (apt-get update -q && apt-get install -y -q openssh-client)) >/dev/null 2>&1 || true
fi

if ! command -v ssh-keygen >/dev/null 2>&1; then
  echo "git-signing: ssh-keygen isn't available, so this session's commits won't be signed" >&2
  exit 0
fi

key="$HOME/.ssh/miccontrol_cloud_signing"
mkdir -p "$HOME/.ssh"
chmod 700 "$HOME/.ssh"
(umask 077 && printf '%s' "$MICCONTROL_GIT_SIGNING_KEY" | base64 -d > "$key")
if ! ssh-keygen -y -f "$key" > "$key.pub" 2>/dev/null; then
  rm -f "$key" "$key.pub"
  echo "git-signing: MICCONTROL_GIT_SIGNING_KEY isn't a usable private key, so this session's commits won't be signed" >&2
  exit 0
fi

git config --global gpg.format ssh
git config --global user.signingkey "$key"
git config --global commit.gpgsign true
git config --global tag.gpgsign true
echo "git-signing: commits are signed with $(ssh-keygen -l -f "$key.pub" | cut -d' ' -f2)" >&2
