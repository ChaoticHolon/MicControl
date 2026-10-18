<#
.SYNOPSIS
Sets up and uses MicControl's signing keys. Runs in Windows PowerShell 5.1 and PowerShell 7.

.DESCRIPTION
GitSigning     Signs your commits and tags, so GitHub shows them as Verified. Run it once per PC.
CloudKey       Makes the key cloud sessions sign their commits with (.claude/hooks/git-signing.sh).
NewReleaseKey  Makes the release key. The updater only installs releases whose SHA512SUMS it has signed.
SignRelease    Checks a draft release made by the Release workflow, signs its SHA512SUMS and publishes it.
Status         Shows what is set up.

All keys are Ed25519 and made by ssh-keygen (OpenSSH ships with Windows 10 and 11). Private keys stay on this PC,
except the cloud key, which is meant to live in the cloud environment.

.EXAMPLE
tools\Signing.ps1 GitSigning

.EXAMPLE
tools\Signing.ps1 SignRelease v1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('GitSigning', 'CloudKey', 'NewReleaseKey', 'SignRelease', 'Status')]
    [string]$Action,

    [Parameter(Position = 1)]
    [string]$Tag,

    [string]$Repository = 'ChaoticHolon/MicControl'
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'

$Email = 'ChaoticHolon@users.noreply.github.com'
$Namespace = 'miccontrol-release'
$SignersPath = 'docs/release_signers'
$OnWindows = [Environment]::OSVersion.Platform -eq 'Win32NT'
$SshDir = Join-Path $HOME '.ssh'
$GitKey = Join-Path $SshDir 'github_signing_ed25519'
$ReleaseKey = Join-Path (Join-Path $HOME '.miccontrol') 'release_signing_ed25519'

# Windows' own OpenSSH: Git for Windows has its own copy, but only Windows' talks to the Windows SSH agent.
$SshKeygen = 'ssh-keygen'
$SshAdd = 'ssh-add'
if ($OnWindows) {
    $SshKeygen = Join-Path $env:WINDIR 'System32\OpenSSH\ssh-keygen.exe'
    $SshAdd = Join-Path $env:WINDIR 'System32\OpenSSH\ssh-add.exe'
    if (-not (Test-Path $SshKeygen)) {
        throw 'The OpenSSH client is missing: Settings > System > Optional features > Add a feature > OpenSSH Client.'
    }
}

function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path $File -Leaf) $($Arguments -join ' ') failed (exit code $LASTEXITCODE)"
    }
}

# Start-Process passes the argument string as is, so an empty passphrase ("") survives; Windows PowerShell drops
# empty arguments given to & .
function Start-Native([string]$File, [string]$Arguments) {
    return (Start-Process -FilePath $File -ArgumentList $Arguments -NoNewWindow -Wait -PassThru).ExitCode
}

# ssh-keygen -Y verify reads the signed file from standard input. Its exact bytes go in: PowerShell's own
# redirection would re-encode the text and change its line endings.
function Test-Signature([string]$Signers, [string]$Signature, [string]$Message) {
    $info = New-Object System.Diagnostics.ProcessStartInfo $SshKeygen
    $info.Arguments = "-Y verify -f `"$Signers`" -I release@miccontrol -n $Namespace -s `"$Signature`""
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::Start($info)
    $bytes = [IO.File]::ReadAllBytes($Message)
    $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $process.StandardInput.Close()
    $process.WaitForExit()
    return $process.ExitCode
}

function New-SshKey([string]$Path, [string]$Comment, [switch]$NoPassphrase) {
    $arguments = "-t ed25519 -C `"$Comment`" -f `"$Path`""
    if ($NoPassphrase) {
        $arguments += ' -N ""'
    }

    if ((Start-Native $SshKeygen $arguments) -ne 0) {
        throw "ssh-keygen couldn't create $Path"
    }
}

function Test-HasPassphrase([string]$Path) {
    $out = [IO.Path]::GetTempFileName()
    try {
        $options = @{
            FilePath = $SshKeygen; ArgumentList = "-y -P `"`" -f `"$Path`""; NoNewWindow = $true; Wait = $true; PassThru = $true
            RedirectStandardOutput = $out; RedirectStandardError = "$out.err"
        }
        return (Start-Process @options).ExitCode -ne 0
    }
    finally {
        Remove-Item $out, "$out.err" -ErrorAction SilentlyContinue
    }
}

function Get-PublicKey([string]$Path) {
    return (Get-Content "$Path.pub" -Raw).Trim()
}

function Get-Fingerprint([string]$Path) {
    return ((& $SshKeygen -l -f "$Path.pub") -split ' ')[1]
}

function Copy-Text([string]$Text, [string]$What) {
    try {
        Set-Clipboard -Value $Text
        Write-Host "$What is on the clipboard."
    }
    catch {
        Write-Host "$What (copy it from here):"
        Write-Host $Text
    }
}

function Add-ToGitHub([string]$PublicKey, [string]$Title) {
    Copy-Text $PublicKey 'The public key'
    Write-Host ''
    Write-Host 'On the GitHub page that opens:'
    Write-Host "  Title:    $Title"
    Write-Host '  Key type: Signing Key   (not Authentication Key)'
    Write-Host '  Key:      paste, then Add SSH key'
    Start-Process 'https://github.com/settings/ssh/new'
    Read-Host 'Press Enter once GitHub shows the key' | Out-Null
}

function Start-SshAgent {
    $service = Get-Service ssh-agent -ErrorAction SilentlyContinue
    if (-not $service) {
        throw 'The OpenSSH client is missing: Settings > System > Optional features > Add a feature > OpenSSH Client.'
    }

    if ($service.Status -ne 'Running') {
        Write-Host 'Turning on the Windows SSH agent, which keeps the key unlocked for Git (admin prompt, once).'
        Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -Command "Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent"'
        if ((Get-Service ssh-agent).Status -ne 'Running') {
            throw "The SSH agent didn't start."
        }
    }
}

function Enable-GitSigning {
    if (Test-Path $GitKey) {
        Write-Host "Using the existing key $GitKey"
    }
    else {
        New-Item -ItemType Directory -Force $SshDir | Out-Null
        Write-Host 'Creating your commit signing key. Pick a passphrase; the SSH agent remembers it, so Git and Visual Studio never ask.'
        New-SshKey $GitKey "$Email (commit signing)"
    }

    $public = Get-PublicKey $GitKey
    if ($OnWindows) {
        Start-SshAgent
        Invoke-Native $SshAdd @($GitKey)
        $signingKey = "$GitKey.pub"
    }
    else {
        $signingKey = $GitKey
    }

    $allowed = Join-Path $SshDir 'allowed_signers'
    $line = "$Email $public"
    if (-not ((Test-Path $allowed) -and ((Get-Content $allowed) -contains $line))) {
        Add-Content -Path $allowed -Value $line
    }

    $settings = [ordered]@{
        'gpg.format'                  = 'ssh'
        'user.signingkey'             = $signingKey -replace '\\', '/'
        'gpg.ssh.program'             = $SshKeygen -replace '\\', '/'
        'gpg.ssh.allowedSignersFile'  = $allowed -replace '\\', '/'
        'commit.gpgsign'              = 'true'
        'tag.gpgsign'                 = 'true'
    }
    foreach ($name in $settings.Keys) {
        Invoke-Native git @('config', '--global', $name, $settings[$name])
    }

    $current = & git config --global user.email
    if (-not $current) {
        Invoke-Native git @('config', '--global', 'user.name', 'Chaotic Holon')
        Invoke-Native git @('config', '--global', 'user.email', $Email)
    }
    elseif ($current -ne $Email) {
        Write-Warning "Git's user.email is $current. GitHub only shows Verified when the commit's email is one of your account's verified addresses."
    }

    Add-ToGitHub $public "$env:COMPUTERNAME commits"
    Write-Host 'Done: new commits and tags are signed. Check one with: git log --show-signature -1'
}

function New-CloudKey {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('miccontrol-cloud-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $dir | Out-Null
    try {
        $key = Join-Path $dir 'cloud_signing_ed25519'
        New-SshKey $key "$Email (cloud sessions)" -NoPassphrase
        Write-Host "Cloud signing key $(Get-Fingerprint $key)"
        Add-ToGitHub (Get-PublicKey $key) 'Claude cloud sessions'

        Copy-Text ([Convert]::ToBase64String([IO.File]::ReadAllBytes($key))) 'The private key (base64)'
        Write-Host ''
        Write-Host 'In claude.ai, open the cloud environment menu in a session''s title bar, then Edit, and add this'
        Write-Host 'environment variable (paste the value after the =):'
        Write-Host '  MICCONTROL_GIT_SIGNING_KEY='
        Write-Host 'Sessions started after that sign their commits.'
        Read-Host 'Press Enter once it is saved (this clears the clipboard and deletes the key from this PC)' | Out-Null
        try { Set-Clipboard -Value ' ' } catch { Write-Verbose 'No clipboard to clear.' }
    }
    finally {
        Remove-Item -Recurse -Force $dir
    }

    Write-Host 'To revoke it: delete "Claude cloud sessions" from github.com/settings/keys and remove the variable.'
}

function New-ReleaseKey {
    if (Test-Path $ReleaseKey) {
        throw "There's already a release key at $ReleaseKey. A second one needs a new name and a release that trusts both (see ReleaseKeys.cs)."
    }

    New-Item -ItemType Directory -Force (Split-Path $ReleaseKey) | Out-Null
    Write-Host 'Creating the release key. Pick a strong passphrase: it is asked at every release, and the file plus the'
    Write-Host 'passphrase is all anyone would need to sign an update that every MicControl installs.'
    New-SshKey $ReleaseKey "MicControl release key $(Get-Date -Format yyyy-MM)"
    if (-not (Test-HasPassphrase $ReleaseKey)) {
        Remove-Item $ReleaseKey, "$ReleaseKey.pub"
        throw 'The release key needs a passphrase; run NewReleaseKey again and set one.'
    }

    $public = Get-PublicKey $ReleaseKey
    Copy-Text $public 'The public key'
    Write-Host ''
    Write-Host "Release key $(Get-Fingerprint $ReleaseKey), saved as $ReleaseKey"
    Write-Host 'Next:'
    Write-Host '  1. Send the public key line to Claude (it is public: it goes into the app and docs/release_signers).'
    Write-Host '  2. Back up BOTH files of the key and the passphrase, apart from this PC (e.g. a password manager'
    Write-Host '     entry with the files attached, plus a USB stick in a drawer). Without them, installed copies'
    Write-Host '     can only get a new key from an update signed by this one.'
}

function Get-Sha512Sums([string]$Path) {
    $entries = @{}
    foreach ($line in Get-Content $Path) {
        if ($line -match '^([0-9a-fA-F]{128}) [ *](.+)$') {
            $entries[$Matches[2]] = $Matches[1].ToLowerInvariant()
        }
        elseif ($line.Trim()) {
            throw "SHA512SUMS has a line that isn't a checksum: $line"
        }
    }

    return $entries
}

function Publish-Release {
    if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Give the release tag, e.g.: tools\Signing.ps1 SignRelease v1.0.0'
    }

    if (-not (Test-Path $ReleaseKey)) {
        throw "No release key at $ReleaseKey (restore your backup there, or run NewReleaseKey)."
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'SignRelease uses the GitHub CLI. Install it and sign in once: winget install --id GitHub.cli -e; gh auth login'
    }

    $release = & gh release view $Tag -R $Repository --json isDraft,assets,url | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "There's no release for $Tag yet: wait for the Release workflow to finish."
    }

    if (-not $release.isDraft) {
        throw "$Tag is already published, and released files are final."
    }

    $names = @($release.assets | ForEach-Object { $_.name })
    if ($names -contains 'SHA512SUMS.sig') {
        throw "The draft for $Tag already has a SHA512SUMS.sig; delete it from the draft to sign again."
    }

    if ($names -notcontains 'SHA512SUMS') {
        throw "The draft for $Tag has no SHA512SUMS."
    }

    $dir = Join-Path ([IO.Path]::GetTempPath()) ('miccontrol-release-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $dir | Out-Null
    try {
        Write-Host "Downloading the draft's files..."
        Invoke-Native gh @('release', 'download', $Tag, '-R', $Repository, '-D', $dir)

        $sums = Join-Path $dir 'SHA512SUMS'
        $listed = Get-Sha512Sums $sums
        foreach ($name in $names | Where-Object { $_ -ne 'SHA512SUMS' }) {
            if (-not $listed.ContainsKey($name)) {
                throw "$name is attached to the draft but not listed in SHA512SUMS."
            }
        }

        foreach ($name in $listed.Keys) {
            $file = Join-Path $dir $name
            if (-not (Test-Path $file)) {
                throw "SHA512SUMS lists $name, which isn't attached to the draft."
            }

            if ((Get-FileHash $file -Algorithm SHA512).Hash.ToLowerInvariant() -ne $listed[$name]) {
                throw "$name doesn't match its SHA512SUMS entry: don't publish this draft."
            }

            Write-Host "  $name matches SHA512SUMS"
        }

        # The key must be one this release trusts; docs/release_signers at the tag lists the same keys as ReleaseKeys.cs.
        $public = Get-PublicKey $ReleaseKey
        $signers = Join-Path $dir 'release_signers'
        & gh api "repos/$Repository/contents/$($SignersPath)?ref=$Tag" -H 'Accept: application/vnd.github.raw' | Set-Content -Path $signers
        $keyField = ($public -split ' ')[1]
        if (-not (Select-String -Path $signers -SimpleMatch $keyField -Quiet)) {
            throw "$Tag doesn't trust the key $ReleaseKey ($SignersPath at that tag doesn't list it), so its updater would refuse its own signature."
        }

        Write-Host "Signing SHA512SUMS with $(Get-Fingerprint $ReleaseKey) (enter the release key's passphrase)..."
        Invoke-Native $SshKeygen @('-Y', 'sign', '-f', $ReleaseKey, '-n', $Namespace, $sums)

        # The same check the updater makes, by OpenSSH itself.
        if ((Test-Signature $signers "$sums.sig" $sums) -ne 0) {
            throw "The new signature doesn't verify against $SignersPath; nothing was published."
        }

        $answer = Read-Host "Publish $Tag ($($release.url)) now? [y/N]"
        if ($answer -notmatch '^[yY]') {
            Write-Host 'Not published; the draft is unchanged.'
            return
        }

        Invoke-Native gh @('release', 'upload', $Tag, "$sums.sig", '-R', $Repository)
        Invoke-Native gh @('release', 'edit', $Tag, '-R', $Repository, '--draft=false', '--latest')
        Write-Host "Published $Tag. Installed copies are offered it on their next update check."
    }
    finally {
        Remove-Item -Recurse -Force $dir
    }
}

function Show-Status {
    foreach ($key in @(@{ Name = 'Commit signing key'; Path = $GitKey }, @{ Name = 'Release key'; Path = $ReleaseKey })) {
        if (Test-Path $key.Path) {
            Write-Host "$($key.Name): $(Get-Fingerprint $key.Path)  ($($key.Path))"
        }
        else {
            Write-Host "$($key.Name): none"
        }
    }

    foreach ($name in 'gpg.format', 'user.signingkey', 'commit.gpgsign', 'tag.gpgsign', 'user.email') {
        Write-Host ("git {0}: {1}" -f $name, (& git config --global $name))
    }

    if ($OnWindows) {
        $service = Get-Service ssh-agent -ErrorAction SilentlyContinue
        $state = if ($service) { $service.Status } else { 'not installed' }
        Write-Host "SSH agent: $state"
    }

    $gh = if (Get-Command gh -ErrorAction SilentlyContinue) { 'installed' } else { 'not installed (needed for SignRelease)' }
    Write-Host "GitHub CLI: $gh"
}

switch ($Action) {
    'GitSigning' { Enable-GitSigning }
    'CloudKey' { New-CloudKey }
    'NewReleaseKey' { New-ReleaseKey }
    'SignRelease' { Publish-Release }
    'Status' { Show-Status }
}
