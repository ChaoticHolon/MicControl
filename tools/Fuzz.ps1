# Runs MicControl's fuzz suite after a build: the opt-in Fuzz tests (parsers, command lines, autostart takeover, huge
# settings files, the hotkey state machine) and the WAV decoding fuzzer. Each run uses a new seed unless -Seed is given;
# a failure prints the seed that repeats it. Works in Windows PowerShell 5.1 and PowerShell 7.
#
#   powershell -ExecutionPolicy Bypass -File tools\Fuzz.ps1              # one pass, new seed
#   powershell -ExecutionPolicy Bypass -File tools\Fuzz.ps1 -Scale 5     # five times the cases
#   powershell -ExecutionPolicy Bypass -File tools\Fuzz.ps1 -Seed 1234   # repeat a failing run
param(
    [double]$Scale = 1,
    [int]$Seed = 0,
    [int]$WavCases = 20000
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
$saved = @{}
$names = 'MICCONTROL_FUZZ', 'MICCONTROL_FUZZ_SEED', 'MICCONTROL_FUZZ_SCALE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'AVALONIA_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO'
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    if ($Seed -eq 0) { $Seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue) }
    $env:MICCONTROL_FUZZ = '1'
    $env:MICCONTROL_FUZZ_SEED = "$Seed"
    $env:MICCONTROL_FUZZ_SCALE = $Scale.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    $env:AVALONIA_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'

    Write-Host "Fuzz suite, seed $Seed, scale $Scale"
    dotnet test --project Sources/MicControl.Tests -- --filter-namespace MicControl.Tests.Fuzz
    $tests = $LASTEXITCODE

    dotnet run tools/FuzzWav.cs $([int]($WavCases * $Scale)) $Seed
    $wav = $LASTEXITCODE

    if ($tests -eq 0 -and $wav -eq 0) {
        Write-Host "Fuzzing passed (seed $Seed)." -ForegroundColor Green
        exit 0
    }

    Write-Host "Fuzzing found problems (seed $Seed): tests exit $tests, WAV fuzzer exit $wav. Rerun with -Seed $Seed to repeat." -ForegroundColor Red
    exit 1
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Pop-Location
}
