param([string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Process Legacy,SpaceEngineers,SEVR.Diagnostics -ErrorAction SilentlyContinue) {
    throw 'Close the game and diagnostics before deployment. No process was stopped.'
}
$targets = @(
    @{ Source=(Join-Path $repo 'SpaceEngineersVR\bin\Debug\net48'); Target=(Join-Path $PrototypeRoot 'Pulsar\Legacy\Local\SpaceEngineersVR'); Name='Plugin' },
    @{ Source=(Join-Path $repo 'tools\SEVR.Diagnostics\bin\Debug\net48'); Target=(Join-Path $PrototypeRoot 'Diagnostics'); Name='Diagnostics' }
)
foreach ($target in $targets) {
    foreach ($asset in @('SpaceEngineersVR.dll', 'openvr_api.dll', 'SEVRAssets\Controls\actions.json')) {
        if (!(Test-Path (Join-Path $target.Source $asset))) { throw "Build is missing $asset. Run Build.ps1 first." }
    }
}
$backup = Join-Path $repo ('.tools\deployment-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($target in $targets) {
    $files = @(Get-ChildItem -LiteralPath $target.Source -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($target.Source.Length).TrimStart('\')
        $destination = Join-Path $target.Target $relative
        if (Test-Path -LiteralPath $destination) {
            $saved = Join-Path (Join-Path $backup $target.Name) $relative
            New-Item -ItemType Directory -Force (Split-Path $saved) | Out-Null
            Copy-Item -LiteralPath $destination -Destination $saved -Force
        }
        New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Installed file does not match the build: $relative"
        }
    }
    Write-Host ($target.Name + ': verified ' + $files.Count + ' files')
}
$controls = Join-Path $repo 'docs\CONTROLS.md'
if (Test-Path $controls) {
    Copy-Item -LiteralPath $controls -Destination (Join-Path $PrototypeRoot 'READ ME FIRST.txt') -Force
}
$dll = Join-Path $targets[0].Target 'SpaceEngineersVR.dll'
Write-Host ('Installed build: ' + (Get-Item $dll).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $dll).Hash)
