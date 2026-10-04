#!/usr/bin/env bash
# Packs the dotnet new templates (templates/), installs them into a template hive of their own, creates an application
# from each, and builds it against the Tenantry packages of the same build, restored from an empty cache with package
# source mapping, as check-package-consumer.sh restores, and with warnings as errors, so the analyzers the packages
# carry must find nothing. Then it runs each: the API must answer a request for a tenant, with a token from its
# development endpoint, and the worker must process its demonstration messages, dropping the one for a tenant the store
# does not have. Each has a minute. Usage, after dotnet pack of src/:
#
#   scripts/smoke-templates.sh <package folder>
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <package folder>" >&2
  exit 2
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
packages="$(cd "$1" && pwd)"
deadline_seconds=60

shopt -s nullglob
core=("$packages"/Tenantry.Core.[0-9]*.nupkg)
if [[ ${#core[@]} -eq 0 ]]; then
  echo "No Tenantry.Core package in $packages" >&2
  exit 1
fi
version="$(unzip -p "${core[0]}" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"

work="$(mktemp -d)"
pid=""
cleanup() {
  [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

dotnet pack "$repo/templates/Tenantry.Templates.csproj" -c Release -o "$work/templates" >/dev/null
template_package=("$work"/templates/Tenantry.Templates.*.nupkg)
template_version="$(unzip -p "${template_package[0]}" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"
if [[ "$template_version" != "$version" ]]; then
  echo "Tenantry.Templates is version $template_version, but the packages in $packages are $version" >&2
  exit 1
fi

hive="$work/hive"
dotnet new install "${template_package[0]}" --debug:custom-hive "$hive" >/dev/null

cp "$repo/global.json" "$work/"
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="Tenantry.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

# An empty cache, so the Tenantry packages come from the package folder.
export NUGET_PACKAGES="$work/packages" DOTNET_NOLOGO=1 DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development

# Waits up to the deadline for the pattern to appear in the log (returns 0), or for the process to exit (returns 1).
# Returns 124 at the deadline.
wait_for() { # <log> <pattern>
  local log="$1" pattern="$2"
  for _ in $(seq 1 $((deadline_seconds * 4))); do
    grep -q "$pattern" "$log" && return 0
    kill -0 "$pid" 2>/dev/null || return 1
    sleep 0.25
  done
  return 124
}

stop() {
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  pid=""
}

failed=0
for template in tenantry-api tenantry-worker; do
  app="$work/$template"
  log="$work/$template.log"
  dotnet new "$template" -n Smoke -o "$app" --debug:custom-hive "$hive" >/dev/null
  if ! grep -q "Version=\"$version\"" "$app/Smoke.csproj"; then
    echo "FAIL $template (does not reference Tenantry $version)" >&2
    failed=1
    continue
  fi
  if ! dotnet build "$app/Smoke.csproj" -c Release -warnaserror > "$log" 2>&1; then
    echo "FAIL $template (build)" >&2
    sed 's/^/     /' "$log" >&2
    failed=1
    continue
  fi

  cd "$app"
  : > "$log"
  if [[ "$template" == tenantry-api ]]; then
    ASPNETCORE_URLS="http://127.0.0.1:0" dotnet bin/Release/net10.0/Smoke.dll > "$log" 2>&1 &
    pid=$!
    url="" status=""
    if wait_for "$log" "Now listening on: http:"; then
      url=$(sed -n 's/.*Now listening on: \(http:[^ ]*\).*/\1/p' "$log" | head -n 1)
      token=$(curl -s --max-time 30 -X POST "$url/dev/token" -H 'Content-Type: application/json' \
        -d '{"subject":"smoke","tenants":["acme"]}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' || true)
      status=$(curl -s --max-time 30 -o /dev/null -w '%{http_code}' -H "X-Tenant-Id: acme" \
        -H "Authorization: Bearer $token" "$url/notes" || true)
    fi
    stop
    if [[ "$status" == 200 ]]; then
      echo "ok   $template (GET /notes for acme: 200)"
    else
      echo "FAIL $template (${url:-did not start listening}; GET /notes for acme: ${status:-no response})" >&2
      sed 's/^/     /' "$log" >&2
      failed=1
    fi
  else
    dotnet bin/Release/net10.0/Smoke.dll > "$log" 2>&1 &
    pid=$!
    status=0
    wait_for "$log" "for tenant globex" || status=$?
    stop
    if [[ $status -eq 0 ]] && grep -q "for tenant acme" "$log" && grep -q "message for tenant initech" "$log"; then
      echo "ok   $template (processed acme's and globex's messages, dropped initech's)"
    else
      echo "FAIL $template ($([[ $status -eq 124 ]] && echo "no message processed in ${deadline_seconds}s" || echo "did not process the messages"))" >&2
      sed 's/^/     /' "$log" >&2
      failed=1
    fi
  fi
  cd "$repo"
done

exit $failed
