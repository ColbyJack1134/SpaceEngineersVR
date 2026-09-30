param(
    [switch]$VR,
    [string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype')
)
$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $PrototypeRoot 'settings.json') -Raw | ConvertFrom-Json
$report = Join-Path $PrototypeRoot ('Reports\preflight-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
$probeArgs = @($settings.GameBinPath)
if ($VR) { $probeArgs += '--vr' }
& (Join-Path $PrototypeRoot 'Diagnostics\SEVR.Diagnostics.exe') @probeArgs 2>&1 | Tee-Object -FilePath $report
$result = $LASTEXITCODE
if ($result -eq 0) { Write-Host 'PASS: compatibility/runtime checks passed. This does not verify image quality or controller buttons.' -ForegroundColor Green }
else { Write-Host "FAIL: preflight exit code $result. See the report below." -ForegroundColor Red }
Write-Host "Report: $report"
exit $result
