param(
    [string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype'),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Process Legacy,Interim,SpaceEngineers,SEVR.Diagnostics -ErrorAction SilentlyContinue) {
    throw 'Close the game and diagnostics before deployment. No process was stopped.'
}
$targets = @(
    @{ Source=(Join-Path $repo "SpaceEngineersVR\bin\$Configuration\net48"); Target=(Join-Path $PrototypeRoot 'Pulsar\Legacy\Local\SpaceEngineersVR'); Name='Plugin' },
    @{ Source=(Join-Path $repo "tools\SEVR.Diagnostics\bin\$Configuration\net48"); Target=(Join-Path $PrototypeRoot 'Diagnostics'); Name='Diagnostics' },
    @{ Source=$PSScriptRoot; Target=$PrototypeRoot; Name='Launchers'; Files=@('Launch.ps1','Preflight.ps1','Launch VR.cmd','Launch Diagnostics.cmd','Check VR.cmd') }
)
foreach ($target in $targets) {
    $required = if ($target.Files) { $target.Files } else { @('SpaceEngineersVR.dll', 'openvr_api.dll', 'SEVRAssets\Controls\actions.json') }
    foreach ($asset in $required) {
        if (!(Test-Path (Join-Path $target.Source $asset))) { throw "Build is missing $asset. Run Build.ps1 first." }
    }
}
foreach ($target in $targets) {
    $files = if ($target.Files) { @($target.Files | ForEach-Object { Get-Item -LiteralPath (Join-Path $target.Source $_) }) } else { @(Get-ChildItem -LiteralPath $target.Source -Recurse -File) }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($target.Source.Length).TrimStart('\')
        $destination = Join-Path $target.Target $relative
        New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Installed file does not match the build: $relative"
        }
    }
    Write-Host ($target.Name + ': verified ' + $files.Count + ' files')
}
# Pulsar Interim compiles this copy at launch; mirror it so deleted files stop compiling.
$source = Join-Path $PrototypeRoot 'Source'
robocopy (Join-Path $repo 'SpaceEngineersVR') (Join-Path $source 'SpaceEngineersVR') /MIR /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Source copy failed (robocopy $LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $repo 'SpaceEngineersVR.xml') -Destination $source -Force
# NVIDIA DLSS runtime: not in Git. PluginHub installs it as a Bin asset; local builds look beside the plugin and in the assets folder.
$runtime = Join-Path $repo '.tools\dlss\runtime\nvngx_dlss.dll'
$runtimeHash = 'BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E'
if (Test-Path -LiteralPath $runtime) {
    if ((Get-FileHash -LiteralPath $runtime).Hash -ne $runtimeHash) { throw 'nvngx_dlss.dll in .tools/dlss/runtime does not match the expected SHA256.' }
    foreach ($destination in @((Join-Path $targets[0].Target 'nvngx_dlss.dll'), (Join-Path $source 'SpaceEngineersVR\Assets\nvngx_dlss.dll'))) {
        if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination).Hash -ne $runtimeHash) { Copy-Item -LiteralPath $runtime -Destination $destination -Force }
    }
    Write-Host 'DLSS runtime: nvngx_dlss.dll installed (plugin folder and Source assets)'
} else {
    Write-Host 'DLSS runtime: .tools\dlss\runtime\nvngx_dlss.dll missing; DLSS stays unavailable in this install'
}
Write-Host ('Source: mirrored ' + @(Get-ChildItem -LiteralPath $source -Recurse -File).Count + ' files')
$controls = Join-Path $repo 'docs\CONTROLS.md'
if (Test-Path $controls) {
    Copy-Item -LiteralPath $controls -Destination (Join-Path $PrototypeRoot 'READ ME FIRST.txt') -Force
}
$dll = Join-Path $targets[0].Target 'SpaceEngineersVR.dll'
Write-Host ('Installed build: ' + (Get-Item $dll).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $dll).Hash)
