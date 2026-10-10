<#
.SYNOPSIS
Builds the setup: artifacts\MicControlNG-Setup-<version>.exe

.DESCRIPTION
The setup is the app itself, published as one self-contained file. Started under its setup name it shows the
installer; once installed it is MicControlNG.exe. No installer framework is involved (see SetupCommand and
WindowsInstaller).

.EXAMPLE
installer\build.ps1                  # version from Sources\Directory.Build.props
installer\build.ps1 -Version 2.1.0
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Sources\Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$publish = Join-Path $root 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'Sources\MicControlNG\MicControlNG.csproj') -c Release -p:PublishSingleFile=true "-p:Version=$Version" -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

$setup = Join-Path $root "artifacts\MicControlNG-Setup-$Version.exe"
Copy-Item (Join-Path $publish 'MicControlNG.exe') $setup -Force
Get-Item $setup
