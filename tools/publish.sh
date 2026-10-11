#!/usr/bin/env bash
# (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
set -euo pipefail
command -v dotnet >/dev/null 2>&1 || { printf 'Install the .NET SDK 10 or later.\n' >&2; exit 1; }
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
exec dotnet run --project "$root/tools/ReleasePackager/ReleasePackager.csproj" -c Release -- "$@"
