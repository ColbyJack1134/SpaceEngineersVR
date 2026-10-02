param([string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype'),[switch]$CockpitsOnly,[string]$Cockpit)
$ErrorActionPreference = 'Stop'
if (Get-Process Legacy,SpaceEngineers -ErrorAction SilentlyContinue) {
    throw 'A game is already running. Physical renderer test did not launch or stop anything.'
}
$started = Get-Date
$previousScope = $env:SEVR_PHYSICAL_COCKPITS_ONLY
$previousCockpit = $env:SEVR_PHYSICAL_COCKPIT
try {
    $env:SEVR_PHYSICAL_COCKPITS_ONLY = if ($CockpitsOnly -or $Cockpit) { '1' } else { $null }
    $env:SEVR_PHYSICAL_COCKPIT = $Cockpit
    $process = & (Join-Path $PSScriptRoot 'Launch.ps1') -PhysicalRenderTest -PassThru -PrototypeRoot $PrototypeRoot
} finally {
    $env:SEVR_PHYSICAL_COCKPITS_ONLY = $previousScope
    $env:SEVR_PHYSICAL_COCKPIT = $previousCockpit
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
Write-Host "PASS: native cockpit material replacement and articulated left/right scene renders. No world or VR session was loaded. Log: $logPath"
