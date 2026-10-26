#!/usr/bin/env bash
# Compiles SEVR the way Pulsar does on native Linux (net10.0, C# 14, LINUX and NETCOREAPP); the Release build never compiles the LINUX code.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
bin64=${SEVR_GAME_BIN:-"/mnt/c/Program Files (x86)/Steam/steamapps/common/SpaceEngineers/Bin64"}
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
# The repository pins SDK 9; building from a folder that selects SDK 10 provides the C# 14 compiler.
printf '{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}' > "$out/global.json"
cd "$out"
# Scratch lock file, restore folder and outputs keep the net48 restore untouched; a build's implicit restore drops the TargetFramework override, so restore runs first.
dotnet=${SEVR_DOTNET:-$HOME/.dotnet/dotnet}
project="$root/SpaceEngineersVR/SpaceEngineersVR.csproj"
props=(-p:TargetFramework=net10.0 -p:LangVersion=14 -p:DefineConstants=TRACE%3BNETCOREAPP%3BLINUX -p:NoWarn=CA1416
    "-p:NuGetLockFilePath=$out/packages.lock.json" "-p:AdditionalExplicitAssemblyReferences=$bin64/System.Memory.dll"
    "-p:MSBuildProjectExtensionsPath=$out/obj/" "-p:OutputPath=$out/bin/" "-p:IntermediateOutputPath=$out/obj/")
"$dotnet" restore "$project" --nologo -v q "${props[@]}"
"$dotnet" build "$project" -c Release --no-restore --nologo -v q "${props[@]}"
if ! grep -aq VrClientPatch "$out/bin/SpaceEngineersVR.dll"; then
    echo "FAIL: Linux code was not compiled."
    exit 1
fi
if [ -n "${SEVR_LINUX_OUT:-}" ]; then cp "$out/bin/SpaceEngineersVR.dll" "$SEVR_LINUX_OUT"; fi
echo "PASS: Linux C# 14 build compiles with LINUX defined."
