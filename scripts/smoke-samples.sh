#!/usr/bin/env bash
# Starts every sample in the Development environment (so the host validates its service registrations and scopes
# when it is built), after `dotnet build -c Release`. Each runs from a fresh copy of its build output, so databases
# the samples create start empty and stay out of the repository.
#   - a web sample must start listening and answer one request for a tenant with 200 (SecureApi's with a token
#     from its development token endpoint, so authentication and the tenant claim check run too);
#   - a console sample must run to completion and exit 0.
# Each sample has a minute to do so. Usage:
#
#   scripts/smoke-samples.sh [framework]      (default net10.0)
#
# A sample that is not listed below fails the script, so a new sample cannot skip it.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
framework="${1:-net10.0}"
deadline_seconds=60

# name | kind | path | tenant (web samples: GET path with the X-Tenant-Id header; "token" kind first asks the
# sample's POST /dev/token for a bearer token that lists the tenant)
samples=(
  "Tenantry.Samples.Aot|web|/orders|acme"
  "Tenantry.Samples.DatabasePerTenant|console"
  "Tenantry.Samples.EfCoreConsole|console"
  "Tenantry.Samples.EfCoreWeb|web|/orders|acme"
  "Tenantry.Samples.Quickstart|web|/orders|acme"
  "Tenantry.Samples.SecureApi|token|/notes|acme"
)

listed=$(printf '%s\n' "${samples[@]}" | cut -d'|' -f1 | sort)
present=$(cd "$repo/samples" && for dir in ./*/; do dir="${dir%/}"; echo "${dir#./}"; done | sort)
if [[ "$listed" != "$present" ]]; then
  echo "scripts/smoke-samples.sh lists different samples from samples/:" >&2
  diff <(echo "$listed") <(echo "$present") >&2 || true
  exit 1
fi

work="$(mktemp -d)"
pid=""
cleanup() {
  [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

export DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development DOTNET_NOLOGO=1

# Waits up to the deadline for the process to exit (returns its exit code) or, with a pattern, for the pattern to
# appear in the log (returns 0). Returns 124 at the deadline.
wait_for() { # <log> [pattern]
  local log="$1" pattern="${2:-}"
  for _ in $(seq 1 $((deadline_seconds * 4))); do
    if [[ -n "$pattern" ]] && grep -q "$pattern" "$log"; then
      return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      [[ -n "$pattern" ]] && return 1
      wait "$pid"
      return
    fi
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
for entry in "${samples[@]}"; do
  IFS='|' read -r name kind path tenant <<<"$entry"
  log="$work/$name.log"
  : > "$log"
  cp -R "$repo/samples/$name/bin/Release/$framework" "$work/$name"
  cd "$work/$name"

  if [[ "$kind" == console ]]; then
    dotnet "$name.dll" > "$log" 2>&1 &
    pid=$!
    status=0
    wait_for "$log" || status=$?
    [[ $status -eq 124 ]] && stop
    pid=""
    if [[ $status -eq 0 ]]; then
      echo "ok   $name (ran to completion)"
    else
      echo "FAIL $name ($([[ $status -eq 124 ]] && echo "still running after ${deadline_seconds}s" || echo "exit code $status"))" >&2
      sed 's/^/     /' "$log" >&2
      failed=1
    fi
    continue
  fi

  ASPNETCORE_URLS="http://127.0.0.1:0" dotnet "$name.dll" > "$log" 2>&1 &
  pid=$!
  url=""
  if wait_for "$log" "Now listening on: http:"; then
    url=$(sed -n 's/.*Now listening on: \(http:[^ ]*\).*/\1/p' "$log" | head -n 1)
  fi

  status=""
  if [[ -n "$url" ]]; then
    headers=(-H "X-Tenant-Id: $tenant")
    if [[ "$kind" == token ]]; then
      token=$(curl -s --max-time 30 -X POST "$url/dev/token" -H 'Content-Type: application/json' \
        -d "{\"subject\":\"smoke\",\"tenants\":[\"$tenant\"]}" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' || true)
      headers+=(-H "Authorization: Bearer $token")
    fi
    status=$(curl -s --max-time 30 -o "$work/$name.body" -w '%{http_code}' "${headers[@]}" "$url$path" || true)
  fi
  stop

  if [[ "$status" == 200 ]]; then
    echo "ok   $name (GET $path for $tenant: 200)"
  else
    echo "FAIL $name (${url:-did not start listening}; GET $path for $tenant: ${status:-no response})" >&2
    sed 's/^/     /' "$log" >&2
    failed=1
  fi
done

exit $failed
