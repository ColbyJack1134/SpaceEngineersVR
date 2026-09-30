param([string]$GameBinPath = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $repo 'tools\SEVR.Diagnostics\bin\Debug\net48\SEVR.Diagnostics.exe') $GameBinPath --ui-test
exit $LASTEXITCODE
