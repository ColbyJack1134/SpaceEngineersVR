param([string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype'))
$ErrorActionPreference = 'Stop'
if (Get-Process Legacy,SpaceEngineers -ErrorAction SilentlyContinue) {
    throw 'A game is already running. Patch smoke test did not launch or stop anything.'
}
$started = Get-Date
$process = & (Join-Path $PSScriptRoot 'Launch.ps1') -PatchTest -PassThru -PrototypeRoot $PrototypeRoot
$passed = $false
$logPath = $null
try {
    $deadline = $started.AddSeconds(90)
    while ((Get-Date) -lt $deadline -and !$process.HasExited) {
        $log = Get-ChildItem (Join-Path $PrototypeRoot 'GameData\SpaceEngineers*.log') |
            Where-Object { $_.CreationTime -ge $started } | Sort-Object CreationTime -Descending | Select-Object -First 1
        if ($log) {
            $logPath = $log.FullName
            if (Select-String -Path $logPath -Pattern 'PATCH SMOKE TEST PASSED' -Quiet) { $passed = $true; break }
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
if (!$passed) { throw "In-game patch smoke failed or timed out. Log: $logPath" }
Write-Host "PASS: in-game renderer/input patch attachment. VR execution stayed disabled. Log: $logPath"
