param([string]$GameBinPath = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & dotnet build SpaceEngineersVR.sln -c Debug --nologo "-p:GameBinPath=$GameBinPath"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }
