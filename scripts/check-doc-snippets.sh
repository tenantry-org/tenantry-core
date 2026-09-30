#!/usr/bin/env bash
# Builds every ```csharp block in the README, docs/*.md and samples/*/README.md against freshly packed packages,
# restored the way an application restores them (an empty cache, and package source mapping that sends the given
# patterns to the package folder and everything else to nuget.org), and fails on any compiler error, reported at
# the block's line in the markdown. Usage:
#
#   scripts/check-doc-snippets.sh <package folder> <pattern>...
#   scripts/check-doc-snippets.sh artifacts 'Tenantry.Pro' 'Tenantry.Pro.*'
#
# The first pattern names a package whose version the project references. In Tenantry.Pro, the project also
# references Tenantry core at the version the repository builds against; to check against an unreleased core, put
# its packages in the folder too, add their patterns, and set TENANTRY_CORE_VERSION:
#
#   TENANTRY_CORE_VERSION=0.5.0-dev scripts/check-doc-snippets.sh packages 'Tenantry.Pro' 'Tenantry.Pro.*' \
#     'Tenantry.Core' 'Tenantry.AspNetCore' 'Tenantry.EfCore'
#
# scripts/doc-snippets.cs extracts the blocks (how each is wrapped is described there) and eng/doc-snippets holds
# the project they build in. Mark a block that is not meant to compile with ```csharp no-compile.
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <package folder> <pattern>..." >&2
  exit 2
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
packages="$(cd "$1" && pwd)"
shift
project="$repo/eng/doc-snippets"

shopt -s nullglob
nupkgs=("$packages/$1".[0-9]*.nupkg)
if [[ ${#nupkgs[@]} -eq 0 ]]; then
  echo "No $1 package in $packages" >&2
  exit 1
fi
version="$(unzip -p "${nupkgs[0]}" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"

work="$(mktemp -d)"
trap '[[ -n "${KEEP_WORK:-}" ]] || rm -rf "$work"' EXIT
cp "$project"/*.csproj "$project"/*.cs "$work"/
cp "$repo/global.json" "$work"/

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<configuration>'
  echo '  <packageSources>'
  echo '    <clear />'
  echo "    <add key=\"local\" value=\"$packages\" />"
  echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />'
  echo '  </packageSources>'
  echo '  <packageSourceMapping>'
  echo '    <packageSource key="local">'
  for pattern in "$@"; do
    echo "      <package pattern=\"$pattern\" />"
  done
  echo '    </packageSource>'
  echo '    <packageSource key="nuget.org">'
  echo '      <package pattern="*" />'
  echo '    </packageSource>'
  echo '  </packageSourceMapping>'
  echo '</configuration>'
} > "$work/nuget.config"

# The project may also reference Tenantry core at the version this repository builds against (Pro does).
properties=(-p:TenantryVersion="$version")
core_version="${TENANTRY_CORE_VERSION:-$(dotnet msbuild "$(ls "$repo"/src/*/*.csproj | head -n 1)" -getProperty:TenantryCoreVersion)}"
[[ -n "$core_version" ]] && properties+=(-p:TenantryCoreVersion="$core_version")

(cd "$repo" && dotnet run scripts/doc-snippets.cs -- "$work/Snippets")

# An empty cache, so the Tenantry packages come from the package folder and not from an earlier restore.
export NUGET_PACKAGES="$work/packages"
dotnet restore "$work/DocSnippets.csproj" --configfile "$work/nuget.config" "${properties[@]}" -v quiet
if ! dotnet build "$work/DocSnippets.csproj" --no-restore "${properties[@]}" -nologo -v quiet \
    -clp:ErrorsOnly -clp:NoSummary > "$work/build.log" 2>&1; then
  # Errors carry the markdown's path through #line; show it relative to the repository, once each.
  sed -e "s:$repo/::g" -e 's: \[[^]]*\.csproj\]$::' "$work/build.log" | sort -u >&2
  echo "The documentation's code blocks do not build against $version" >&2
  exit 1
fi
echo "The documentation's code blocks build against $version"
