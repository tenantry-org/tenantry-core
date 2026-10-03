#!/usr/bin/env bash
# Builds and runs eng/package-consumer against Tenantry packages the way an application consumes them:
# outside the repository, from an empty NuGet cache, and with package source mapping that sends the given
# package patterns to one source and everything else to nuget.org. The source is either a folder of freshly
# packed packages (CI), or a published feed as a customer sees it (the release, after publishing). Usage:
#
#   scripts/check-package-consumer.sh <package folder> <pattern>...
#   scripts/check-package-consumer.sh artifacts 'Tenantry.Pro' 'Tenantry.Pro.*'
#
#   FEED_USER=... FEED_TOKEN=... scripts/check-package-consumer.sh --feed <url> <version> <pattern>...
#
# With --feed, the credentials come from FEED_USER and FEED_TOKEN, which the generated nuget.config names
# rather than contains, so the token is never written to disk. With TENANTRY_CORE_PACKAGES set (a folder of
# locally packed Tenantry Core packages, see scripts/pack-local-core.sh), Tenantry Core comes from that folder.
set -euo pipefail

usage="usage: $0 <package folder> <pattern>... | $0 --feed <url> <version> <pattern>..."
repo="$(cd "$(dirname "$0")/.." && pwd)"
consumer="$repo/eng/package-consumer"

if [[ "${1:-}" == "--feed" ]]; then
  [[ $# -ge 4 ]] || { echo "$usage" >&2; exit 2; }
  source_value="$2"
  version="$3"
  shift 3
  : "${FEED_USER:?FEED_USER is not set}" "${FEED_TOKEN:?FEED_TOKEN is not set}"
  # A pasted secret can carry a newline, which cannot go in an HTTP header: NuGet then fails without saying why.
  for name in FEED_USER FEED_TOKEN; do
    if [[ "${!name}" =~ [[:space:]] ]]; then
      echo "$name contains whitespace (a trailing newline?); set the secret again without it" >&2
      exit 1
    fi
  done
  restore_verbosity=(-v normal)
else
  [[ $# -ge 2 ]] || { echo "$usage" >&2; exit 2; }
  packages="$(cd "$1" && pwd)"
  source_value="$packages"
  shift
  version=""
  restore_verbosity=()
fi

# From a folder: every package must be referenced by the consumer (so a new package cannot skip this
# check), and all of them must carry the same version, which the consumer then references exactly.
shopt -s nullglob
nupkgs=()
if [[ -z "$version" ]]; then
  nupkgs=("$packages"/*.nupkg)
  if [[ ${#nupkgs[@]} -eq 0 ]]; then
    echo "No packages in $packages" >&2
    exit 1
  fi
fi
for nupkg in ${nupkgs[@]+"${nupkgs[@]}"}; do  # empty with --feed; bash 3.2 (macOS) rejects an empty "${a[@]}" under set -u
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
# The project and its sources only: a local build leaves bin/ and obj/ beside them.
find "$consumer" -maxdepth 1 -type f -exec cp {} "$work"/ \;
cp "$repo/global.json" "$work"/

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<configuration>'
  echo '  <packageSources>'
  echo '    <clear />'
  echo "    <add key=\"local\" value=\"$source_value\" />"
  if [[ -n "${TENANTRY_CORE_PACKAGES:-}" ]]; then
    echo "    <add key=\"tenantry-core\" value=\"$TENANTRY_CORE_PACKAGES\" />"
  fi
  echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />'
  echo '  </packageSources>'
  if [[ -z "${packages:-}" ]]; then
    echo '  <packageSourceCredentials>'
    echo '    <local>'
    echo '      <add key="Username" value="%FEED_USER%" />'
    echo '      <add key="ClearTextPassword" value="%FEED_TOKEN%" />'
    echo '    </local>'
    echo '  </packageSourceCredentials>'
  fi
  echo '  <packageSourceMapping>'
  echo '    <packageSource key="local">'
  for pattern in "$@"; do
    echo "      <package pattern=\"$pattern\" />"
  done
  echo '    </packageSource>'
  if [[ -n "${TENANTRY_CORE_PACKAGES:-}" ]]; then
    echo '    <packageSource key="tenantry-core">'
    for pattern in Tenantry.Core Tenantry.EfCore Tenantry.AspNetCore Tenantry.Http Tenantry.Caching; do
      echo "      <package pattern=\"$pattern\" />"
    done
    echo '    </packageSource>'
  fi
  echo '    <packageSource key="nuget.org">'
  echo '      <package pattern="*" />'
  echo '    </packageSource>'
  echo '  </packageSourceMapping>'
  echo '</configuration>'
} > "$work/nuget.config"

# An empty cache, so every package is restored from one of the two sources.
export NUGET_PACKAGES="$work/packages"

if ! dotnet restore "$work/PackageConsumer.csproj" --configfile "$work/nuget.config" -p:TenantryVersion="$version" \
  ${restore_verbosity[@]+"${restore_verbosity[@]}"}; then
  # NuGet sometimes fails without logging why (MSB4181); the reasons are still in the assets file.
  assets="$work/obj/project.assets.json"
  [[ -f "$assets" ]] && grep -o '"message": "[^"]*"' "$assets" | sort -u >&2
  exit 1
fi
dotnet build "$work/PackageConsumer.csproj" -c Release --no-restore -p:TenantryVersion="$version"

for output in "$work"/bin/Release/*/; do
  echo "Running the consumer on $(basename "$output")"
  dotnet "$output/PackageConsumer.dll"
done

echo "The consumer restored, built and ran against $version"
