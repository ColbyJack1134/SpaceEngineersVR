param(
    [string]$GameBinPath = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$PrototypeRoot = (Join-Path $env:LOCALAPPDATA 'SEVRPrototype')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Process Legacy,SpaceEngineers -ErrorAction SilentlyContinue) {
    throw 'A game is running. Close it before deploying; no process was stopped.'
}
foreach ($process in @(Get-Process Legacy,SEVR.Diagnostics -ErrorAction SilentlyContinue)) {
    if ($process.Path -and $process.Path.StartsWith($PrototypeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Close the running SEVR prototype/diagnostic before deploying.'
    }
}
if (!(Test-Path (Join-Path $GameBinPath 'SpaceEngineers.exe'))) { throw 'Space Engineers Bin64 not found.' }
$output = Join-Path $repo 'tools\SEVR.Diagnostics\bin\Debug\net48'
if (!(Test-Path (Join-Path $output 'SEVR.Diagnostics.exe'))) { throw 'Run Build.ps1 or scripts/build.sh first.' }
foreach ($asset in @('SEVRAssets\icon.ico', 'SEVRAssets\logo.png', 'SEVRAssets\Controls\actions.json', 'SEVRAssets\Controls\binding_oculus_touch.json', 'openvr_api.dll')) {
    if (!(Test-Path (Join-Path $output $asset))) { throw "Build is missing required asset: $asset" }
}
$archive = Join-Path $repo '.tools\Pulsar-v2.4.2-win-x64.zip'
New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
if (!(Test-Path $archive)) {
    Invoke-WebRequest -UseBasicParsing 'https://github.com/SpaceGT/Pulsar/releases/download/v2.4.2/Pulsar-v2.4.2-win-x64.zip' -OutFile $archive
}
$expected = '002b033781ffcb9f325b91df426fd20e8da33b9d0cf79c52220e08b7fa3127ff'
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Pulsar archive checksum mismatch.' }
$pulsar = Join-Path $PrototypeRoot 'Pulsar'
$plugin = Join-Path $pulsar 'Legacy\Local\SpaceEngineersVR'
$diagnostics = Join-Path $PrototypeRoot 'Diagnostics'
$data = Join-Path $PrototypeRoot 'GameData'
foreach ($dir in @($pulsar, $plugin, $diagnostics, $data, (Join-Path $PrototypeRoot 'Reports'))) {
    New-Item -ItemType Directory -Force $dir | Out-Null
}
# Pulsar has its own directory; source and test saves live outside it. Updates are disabled.
if (!(Test-Path (Join-Path $pulsar 'Legacy.exe'))) { Expand-Archive $archive $pulsar }
& (Join-Path $PSScriptRoot 'Deploy.ps1') -PrototypeRoot $PrototypeRoot
New-Item -ItemType Directory -Force (Join-Path $pulsar 'Legacy\Sources'), (Join-Path $pulsar 'Legacy\Profiles') | Out-Null
@'
<?xml version="1.0"?>
<SourcesConfig>
  <ShowWarning>true</ShowWarning>
  <LocalHubSources /><RemoteHubSources /><RemotePluginSources /><ModSources />
  <LocalPluginSources />
</SourcesConfig>
'@ | Set-Content (Join-Path $pulsar 'Legacy\Sources\sources.xml') -Encoding UTF8
@'
<?xml version="1.0"?>
<Profile><Name>SEVR Prototype</Name><GitHub /><DevFolder /><Local><string>SpaceEngineersVR.dll</string></Local><Mods /></Profile>
'@ | Set-Content (Join-Path $pulsar 'Legacy\Profiles\sevr.xml') -Encoding UTF8
@{ GameBinPath = $GameBinPath; PrototypeRoot = $PrototypeRoot; SourceRoot = $repo; PulsarVersion = '2.4.2' } |
    ConvertTo-Json | Set-Content (Join-Path $PrototypeRoot 'settings.json') -Encoding UTF8
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'Space Engineers VR Prototype.lnk'))
$shortcut.TargetPath = Join-Path $PrototypeRoot 'Launch VR.cmd'
$shortcut.WorkingDirectory = $PrototypeRoot
$shortcut.IconLocation = (Join-Path $GameBinPath 'SpaceEngineers.exe') + ',0'
$shortcut.Description = 'Quest 3 / SteamVR experimental Space Engineers 1 plugin'
$shortcut.Save()
Write-Host "Prototype staged at $PrototypeRoot"
Write-Host 'Steam launch options and the normal game profile were not changed.'
