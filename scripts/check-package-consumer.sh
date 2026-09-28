#!/usr/bin/env bash
# Builds and runs eng/package-consumer against freshly packed packages the way an application consumes
# them: outside the repository, from an empty NuGet cache, and with package source mapping that sends the
# given package patterns to the package folder and everything else to nuget.org. Usage:
#
#   scripts/check-package-consumer.sh <package folder> <pattern>...
#   scripts/check-package-consumer.sh artifacts 'Tenantry.Pro' 'Tenantry.Pro.*'
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <package folder> <pattern>..." >&2
  exit 2
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
packages="$(cd "$1" && pwd)"
shift
consumer="$repo/eng/package-consumer"

shopt -s nullglob
nupkgs=("$packages"/*.nupkg)
if [[ ${#nupkgs[@]} -eq 0 ]]; then
  echo "No packages in $packages" >&2
  exit 1
fi

# Every package must be referenced by the consumer (so a new package cannot skip this check), and all
# of them must carry the same version, which the consumer then references exactly.
version=""
for nupkg in "${nupkgs[@]}"; do
  nuspec="$(unzip -p "$nupkg" '*.nuspec')"
  id="$(sed -n 's:.*<id>\(.*\)</id>.*:\1:p' <<<"$nuspec" | head -n 1)"
  package_version="$(sed -n 's:.*<version>\(.*\)</version>.*:\1:p' <<<"$nuspec" | head -n 1)"
  if ! grep -q "Include=\"$id\"" "$consumer/PackageConsumer.csproj"; then
    echo "eng/package-consumer/PackageConsumer.csproj does not reference $id" >&2
    exit 1
  fi
  if [[ -n "$version" && "$package_version" != "$version" ]]; then
    echo "$id is version $package_version, but other packages are $version" >&2
    exit 1
  fi
  version="$package_version"
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp "$consumer"/* "$work"/
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

# An empty cache, so every package is restored from one of the two sources.
export NUGET_PACKAGES="$work/packages"

dotnet restore "$work/PackageConsumer.csproj" --configfile "$work/nuget.config" -p:TenantryVersion="$version"
dotnet build "$work/PackageConsumer.csproj" -c Release --no-restore -p:TenantryVersion="$version"

for output in "$work"/bin/Release/*/; do
  echo "Running the consumer on $(basename "$output")"
  dotnet "$output/PackageConsumer.dll"
done

echo "The consumer restored, built and ran against $version"
