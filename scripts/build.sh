#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet_bin="${SEVR_DOTNET:-$HOME/.dotnet/dotnet}"
if [[ ! -x "$dotnet_bin" ]]; then dotnet_bin=dotnet; fi
"$dotnet_bin" build tools/SEVR.Diagnostics/SEVR.Diagnostics.csproj -c Debug --nologo "$@"
