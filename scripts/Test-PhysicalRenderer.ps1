param([string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype'),[switch]$CockpitsOnly,[string]$Cockpit,[switch]$InterfaceOnly,[switch]$CameraHudOnly,[switch]$NativeSignals,[switch]$NativeLead)
$ErrorActionPreference = 'Stop'
if (Get-Process Legacy,SpaceEngineers -ErrorAction SilentlyContinue) {
    throw 'A game is already running. Physical renderer test did not launch or stop anything.'
}
$started = Get-Date
$previousScope = $env:SEVR_PHYSICAL_COCKPITS_ONLY
$previousCockpit = $env:SEVR_PHYSICAL_COCKPIT
$previousInterface = $env:SEVR_PHYSICAL_INTERFACE_ONLY
$previousCamera = $env:SEVR_PHYSICAL_CAMERA_HUD_ONLY
$previousSignals = $env:SEVR_NATIVE_SIGNALS
$previousLead = $env:SEVR_NATIVE_LEAD
try {
    $env:SEVR_PHYSICAL_COCKPITS_ONLY = if ($CockpitsOnly -or $Cockpit) { '1' } else { $null }
    $env:SEVR_PHYSICAL_COCKPIT = $Cockpit
    $env:SEVR_PHYSICAL_INTERFACE_ONLY = if ($InterfaceOnly) { '1' } else { $null }
    $env:SEVR_PHYSICAL_CAMERA_HUD_ONLY = if ($CameraHudOnly) { '1' } else { $null }
    $env:SEVR_NATIVE_SIGNALS = if ($NativeSignals -or $NativeLead) { '1' } else { $null }
    $env:SEVR_NATIVE_LEAD = if ($NativeLead) { '1' } else { $null }
    $process = & (Join-Path $PSScriptRoot 'Launch.ps1') -PhysicalRenderTest -PassThru -PrototypeRoot $PrototypeRoot
} finally {
    $env:SEVR_PHYSICAL_COCKPITS_ONLY = $previousScope
    $env:SEVR_PHYSICAL_COCKPIT = $previousCockpit
    $env:SEVR_PHYSICAL_INTERFACE_ONLY = $previousInterface
    $env:SEVR_PHYSICAL_CAMERA_HUD_ONLY = $previousCamera
    $env:SEVR_NATIVE_SIGNALS = $previousSignals
    $env:SEVR_NATIVE_LEAD = $previousLead
}
$passed = $false
$logPath = $null
try {
    $deadline = $started.AddSeconds(550)
    while ((Get-Date) -lt $deadline -and !$process.HasExited) {
        $log = Get-ChildItem (Join-Path $PrototypeRoot 'GameData\SpaceEngineersVR_*.log') |
            Where-Object { $_.CreationTime -ge $started } | Sort-Object CreationTime -Descending | Select-Object -First 1
        if ($log) {
            $logPath = $log.FullName
            if (Select-String -Path $logPath -Pattern 'PHYSICAL RENDER SMOKE PASSED' -Quiet) { $passed = $true; break }
            if (Select-String -Path $logPath -Pattern 'PHYSICAL RENDER SMOKE FAILED' -Quiet) { break }
        }
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
} finally {
    # Close only the process returned by this test's own launch; never terminate an existing game.
    $closeDeadline = (Get-Date).AddSeconds(30)
    while (!$process.HasExited -and $process.MainWindowHandle -eq 0 -and (Get-Date) -lt $closeDeadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(15000)) { Write-Warning "Test process $($process.Id) remains open; it was not killed." }
    }
}
if (!$passed) { throw "Physical renderer probe failed or timed out. Log: $logPath" }
Write-Host "PASS: native renderer probe. InterfaceOnly=$InterfaceOnly CameraHudOnly=$CameraHudOnly NativeSignals=$NativeSignals. No world or VR session was loaded. Log: $logPath"
