#!/usr/bin/env bash
# (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
set -euo pipefail

usage() {
    cat <<'HELP'
Usage: bash build.sh [--configuration Debug|Release] [--test] [--publish]
                     [--runtime linux-x64|linux-arm64|linux-arm|win-x64|win-arm64|osx-x64|osx-arm64]
                     [--framework-dependent] [--help]

Default: restore and build Release. Requires .NET SDK 10 or later.
--test: run .NET and web tests; requires Node.js 18 or later.
--publish: make a ready-to-run Release folder in artifacts/publish/.
           Defaults to the current Linux/macOS processor architecture.
--framework-dependent: omit bundled runtimes; requires --publish.
Builds do not start jaNET or change its saved settings.
HELP
}
fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
configuration=Release
tests=false
publish=false
dependent=false
runtime=''
while (($#)); do
    case "$1" in
        --help|-h) usage; exit 0 ;;
        --test) tests=true; shift ;;
        --publish) publish=true; shift ;;
        --framework-dependent) dependent=true; shift ;;
        --configuration|--runtime)
            (($# >= 2)) && [[ "$2" != --* ]] || fail "$1 needs a value."
            if [[ "$1" == --configuration ]]; then configuration=$2; else runtime=$2; fi
            shift 2 ;;
        *) fail "Unknown option: $1 (use --help)." ;;
    esac
done
case "$configuration" in Debug|Release) ;; *) fail 'Configuration must be Debug or Release.' ;; esac
case "$runtime" in ''|linux-x64|linux-arm64|linux-arm|win-x64|win-arm64|osx-x64|osx-arm64) ;; *) fail "Unsupported runtime: $runtime." ;; esac
if [[ -n "$runtime" || "$dependent" == true ]] && [[ "$publish" != true ]]; then fail '--runtime and --framework-dependent require --publish.'; fi
if [[ "$publish" == true && "$configuration" != Release ]]; then fail 'Publishing uses Release. Omit --configuration Debug when publishing.'; fi
command -v dotnet >/dev/null 2>&1 || fail 'Install .NET SDK 10 or later: https://dotnet.microsoft.com/download'
sdk=$(dotnet --version) || fail 'Install the .NET SDK, not only the runtime.'
[[ "$sdk" =~ ^([0-9]+)\. ]] && ((BASH_REMATCH[1] >= 10)) || fail 'jaNET requires .NET SDK 10 or later.'
if [[ "$tests" == true ]]; then
    command -v node >/dev/null 2>&1 || fail '--test requires Node.js 18 or later for the web tests.'
    node_version=$(node --version)
    [[ "$node_version" =~ ^v([0-9]+)\. ]] && ((BASH_REMATCH[1] >= 18)) || fail '--test requires Node.js 18 or later.'
fi
if [[ "$publish" == true ]]; then
    if [[ -z "$runtime" ]]; then
        case "$(uname -s):$(uname -m)" in
            Linux:x86_64) runtime=linux-x64 ;;
            Linux:aarch64|Linux:arm64) runtime=linux-arm64 ;;
            Linux:armv7l|Linux:armv8l) runtime=linux-arm ;;
            Darwin:x86_64) runtime=osx-x64 ;;
            Darwin:arm64) runtime=osx-arm64 ;;
            *) fail 'Cannot detect a supported architecture. Specify --runtime explicitly.' ;;
        esac
    fi
fi

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd -- "$root"
dotnet restore jaNETFramework.sln --nologo
dotnet build jaNETFramework.sln -c "$configuration" --no-restore --nologo
if [[ "$tests" == true ]]; then
    dotnet test jaNETFramework.sln -c "$configuration" --no-build --no-restore --nologo
    node --test jaNETFramework.Tests/Web*Tests.mjs
fi
if [[ "$publish" == true ]]; then
    self_contained=true
    suffix=''
    if [[ "$dependent" == true ]]; then self_contained=false; suffix=-framework-dependent; fi
    output="$root/artifacts/publish/$runtime$suffix"
    dotnet publish jaNETProgram -c Release -r "$runtime" --self-contained "$self_contained" -o "$output" --nologo
    cp -- LICENSE CHANGELOG.md THIRD-PARTY-NOTICES.md README.md "$output/"
    if [[ "$runtime" == linux-* ]]; then cp -- deploy/janet.service "$output/"; fi
    printf 'Published: %s\n' "$output"
fi
printf 'Build ready: %s/jaNETProgram/bin/%s/net10.0\n' "$root" "$configuration"
printf 'Run: dotnet run --project jaNETProgram -c %s --no-build\n' "$configuration"
