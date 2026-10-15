#!/usr/bin/env bash
# (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
set -euo pipefail

if (( $# < 4 )); then
    echo 'Usage: publish-github-release.sh <tag> <version> <notes-file> <asset>...' >&2
    exit 1
fi
tag=$1
version=$2
notes=$3
shift 3
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ || "$tag" != "v$version" ]]; then
    echo 'The release tag must match the project version.' >&2
    exit 1
fi
: "${GITHUB_REPOSITORY:?Set GITHUB_REPOSITORY to owner/repository.}"
[[ -f "$notes" ]] || { echo "Release notes not found: $notes" >&2; exit 1; }

temporary_root=$(cd "${TMPDIR:-/tmp}" && pwd -P)
staging=$(mktemp -d "$temporary_root/janet-github-release.XXXXXXXX")
cleanup() {
    if [[ "$staging" == "$temporary_root"/janet-github-release.* && -d "$staging" && ! -L "$staging" ]]; then
        rm -rf -- "$staging"
    fi
}
trap cleanup EXIT

for asset in "$@"; do
    [[ -f "$asset" ]] || { echo "Release asset not found: $asset" >&2; exit 1; }
    name=$(basename -- "$asset")
    if [[ "$name" == SHA256SUMS.txt || "$name" == *'#'* || "$name" == *$'\n'* || "$name" == *$'\r'* || -e "$staging/$name" ]]; then
        echo "Duplicate, reserved or invalid release asset name: $name" >&2
        exit 1
    fi
    cp -- "$asset" "$staging/$name"
done
assets=("$staging/"*)
(
    cd "$staging"
    # Hash flat download names, not the build's packages/ or artifacts/ paths.
    sha256sum --binary -- * > SHA256SUMS.txt
)

# Listing succeeds with an empty result for a missing release. Authentication or
# network errors stop here instead of being mistaken for a missing release.
existing=$(gh api --paginate "repos/$GITHUB_REPOSITORY/releases?per_page=100" \
    --jq ".[] | select(.tag_name == \"$tag\") | .id")
if [[ -n "$existing" ]]; then
    echo "Updating assets on existing release $tag; keeping its title, notes and publication state."
    gh release upload "$tag" "${assets[@]}" --repo "$GITHUB_REPOSITORY" --clobber
    # Do not publish new checksums until all replacement files have uploaded.
    gh release upload "$tag" "$staging/SHA256SUMS.txt" --repo "$GITHUB_REPOSITORY" --clobber
else
    flags=()
    [[ "$version" != *-* ]] || flags+=(--prerelease)
    gh release create "$tag" "${flags[@]}" --repo "$GITHUB_REPOSITORY" --verify-tag \
        --title "jaNET Framework $version" --notes-file "$notes" "$staging/"*
fi
