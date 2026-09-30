param(
    [switch]$ExperimentalVR,
    [switch]$PatchTest,
    [switch]$PhysicalRenderTest,
    [switch]$PassThru,
    [string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype')
)
$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $PrototypeRoot 'settings.json') -Raw | ConvertFrom-Json
$env:SEVR_MODE = 'diagnostics'
if (([int]$PatchTest.IsPresent + [int]$ExperimentalVR.IsPresent + [int]$PhysicalRenderTest.IsPresent) -gt 1) { throw 'Choose one diagnostic or VR mode.' }
if ($PatchTest) { $env:SEVR_MODE = 'patchtest' }
if ($PhysicalRenderTest) { $env:SEVR_MODE = 'physicaltest' }
if ($ExperimentalVR) {
    if (!(Get-Process vrserver -ErrorAction SilentlyContinue)) {
        Write-Host 'Starting SteamVR. Connect the Quest using Virtual Desktop.'
        Start-Process 'steam://rungameid/250820'
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if (Get-Process vrserver -ErrorAction SilentlyContinue) { break }
            Start-Sleep -Seconds 1
        }
        Start-Sleep -Seconds 3
    }
    & (Join-Path $PrototypeRoot 'Diagnostics\SEVR.Diagnostics.exe') $settings.GameBinPath --vr
    if ($LASTEXITCODE -eq 3) { throw 'SteamVR is not ready. Connect/wake Quest 3 in Virtual Desktop, start SteamVR, then launch again.' }
    if ($LASTEXITCODE -ne 0) { throw 'VR preflight failed. Run Check VR.cmd for a saved report.' }
    $env:SEVR_MODE = 'render'
}
$pulsar = Join-Path $PrototypeRoot 'Pulsar'
$arguments = @('-bin64', ('"' + $settings.GameBinPath + '"'), '-noUpdate', '-noStats', '-noDiscord', '-bare', '-sources',
    '-profile', ('"' + (Join-Path $pulsar 'Legacy\Profiles\sevr.xml') + '"'),
    '-appdata', ('"' + (Join-Path $PrototypeRoot 'GameData') + '"'))
$launched = Start-Process (Join-Path $pulsar 'Legacy.exe') -WorkingDirectory $pulsar -ArgumentList $arguments -PassThru
Write-Host "Started SEVR in $env:SEVR_MODE mode. Logs and test saves: $PrototypeRoot\GameData"
if ($PassThru) { $launched }
