#!/usr/bin/env bash
# Compiles SEVR the way Pulsar does: C# 14 with the game's System.Memory in scope. Release builds use C# 7.3, so any
# call that binds to a span extension here behaves differently in hub builds; array.SequenceEqual and Reverse did.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
bin64=${SEVR_GAME_BIN:-"/mnt/c/Program Files (x86)/Steam/steamapps/common/SpaceEngineers/Bin64"}
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT
# The repository pins SDK 9; building from a folder that selects SDK 10 provides the C# 14 compiler.
printf '{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}' > "$out/global.json"
cd "$out"
"${SEVR_DOTNET:-$HOME/.dotnet/dotnet}" build "$root/SpaceEngineersVR/SpaceEngineersVR.csproj" -c Release --nologo -v q \
    -p:LangVersion=14 -p:DefineConstants=TRACE "-p:AdditionalExplicitAssemblyReferences=$bin64/System.Memory.dll" \
    "-p:OutputPath=$out/bin/" "-p:IntermediateOutputPath=$out/obj/"
if grep -aq MemoryExtensions "$out/bin/SpaceEngineersVR.dll"; then
    echo "FAIL: calls bind to System.MemoryExtensions under C# 14. Call the Enumerable method explicitly."
    exit 1
fi
if grep -aq RegressionTests "$out/bin/SpaceEngineersVR.dll"; then
    echo "FAIL: diagnostics code compiled without SEVR_DIAGNOSTICS."
    exit 1
fi
if [ -n "${SEVR_PULSAR_OUT:-}" ]; then cp "$out/bin/SpaceEngineersVR.dll" "$SEVR_PULSAR_OUT"; fi
echo "PASS: Pulsar C# 14 build matches release bindings without diagnostics code."
