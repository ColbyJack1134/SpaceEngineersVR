param(
    [Parameter(Mandatory=$true)][string]$Model,
    [string]$GameBinPath = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output = Join-Path $repo '.tools\models' }
$program = Join-Path $repo 'tools\SEVR.Diagnostics\bin\Debug\net48\SEVR.Diagnostics.exe'
if (!(Test-Path $program)) { throw 'Build the project first.' }
& $program $GameBinPath --export-model $Model $Output
exit $LASTEXITCODE
