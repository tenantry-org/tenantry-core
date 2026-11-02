#!/usr/bin/env bash
# Installs the dotnet new templates from the Tenantry.Templates package in the package folder, the one a release
# publishes, into a template hive of their own, creates an application from each, and builds it against the Tenantry
# packages of the same build, restored from an empty cache with package source mapping, as check-package-consumer.sh
# restores, with the .NET 10 SDK, the one the templates' net10.0 projects need, and with warnings as errors, so the
# analyzers the packages carry must find nothing. Then it runs each, for a minute at most. The API, started with dotnet
# run so that its launch profile sets the environment, with tokens from its development endpoint: an anonymous caller
# gets 401, a token for acme gets 403 for globex, acme's new note is listed for acme and not for globex. The worker: it
# processes acme's and globex's first orders, drops the messages for an unknown and a suspended tenant, logs the one
# that fails, and keeps running. It also checks that --TenantryVersion sets the version of every Tenantry reference,
# and that the API template's code is still the SecureApi sample's, which the integration tests test.
# Usage, after dotnet pack of src/ and templates/ (build-test.yml's Pack step):
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

template_package=("$packages"/Tenantry.Templates.[0-9]*.nupkg)
if [[ ${#template_package[@]} -ne 1 ]]; then
  echo "No single Tenantry.Templates package in $packages: pack templates/Tenantry.Templates.csproj into it" >&2
  exit 1
fi
template_version="$(unzip -p "${template_package[0]}" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"
if [[ "$template_version" != "$version" ]]; then
  echo "Tenantry.Templates is version $template_version, but the packages in $packages are $version" >&2
  exit 1
fi

failed=0

# The API template is the SecureApi sample under its own namespace, and the integration tests test only the sample,
# so a change to one must be made to the other. Comments may differ; the code may differ only by these lines, which
# the template adds (HSTS and HTTPS redirection as ASP.NET Core's templates have them, and a using its namespace needs).
sample="$repo/samples/Tenantry.Samples.SecureApi"
api_template="$repo/templates/content/tenantry-api"
template_only=(
  "AuthSettings.cs|"
  "Notes.cs|> using Tenantry;"
  "Program.cs|> if (!app.Environment.IsDevelopment())
> {
>     app.UseHsts();
> }
> app.UseHttpsRedirection();"
)
code() { # <file>: its lines of code, under the template's namespace
  sed -e '/^[[:space:]]*\/\//d' -e '/^[[:space:]]*$/d' -e 's/Tenantry\.Samples\.SecureApi/TenantryApi/g' "$1"
}
for entry in "${template_only[@]}"; do
  file="${entry%%|*}"
  differences=$(diff <(code "$sample/$file") <(code "$api_template/$file") | grep '^[<>]' || true)
  if [[ "$differences" != "${entry#*|}" ]]; then
    echo "FAIL tenantry-api ($file is not samples/Tenantry.Samples.SecureApi/$file: make the change in both, or" \
      "update template_only in $0)" >&2
    diff <(code "$sample/$file") <(code "$api_template/$file") | sed 's/^/     /' >&2 || true
    failed=1
  fi
done

work="$(mktemp -d)"
pid=""
cleanup() {
  [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

hive="$work/hive"
dotnet new install "${template_package[0]}" --debug:custom-hive "$hive" >/dev/null

# The templates' projects target net10.0, so the .NET 10 SDK must build them, as it does for a user who has no later
# one. The builds and runs below start in the project's folder, under this global.json, not the repository's.
cat > "$work/global.json" <<EOF
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
EOF
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

# Whether the project has Tenantry package references, and each is at the version.
references() { # <project> <version>
  local all at
  all=$(grep -c 'Include="Tenantry\.' "$1" || true)
  at=$(grep 'Include="Tenantry\.' "$1" | grep -cF "Version=\"$2\"" || true)
  [[ $all -gt 0 && $at -eq $all ]]
}

for template in tenantry-api tenantry-worker; do
  app="$work/$template"
  log="$work/$template.log"
  pinned="$work/$template-pinned"
  if ! dotnet new "$template" -n Pinned -o "$pinned" --TenantryVersion 9.9.9 --debug:custom-hive "$hive" >/dev/null ||
     ! references "$pinned/Pinned.csproj" 9.9.9; then
    echo "FAIL $template (--TenantryVersion 9.9.9 does not set the version of every Tenantry reference)" >&2
    failed=1
  fi
  dotnet new "$template" -n Smoke -o "$app" --debug:custom-hive "$hive" >/dev/null
  if ! references "$app/Smoke.csproj" "$version"; then
    echo "FAIL $template (does not reference Tenantry $version)" >&2
    failed=1
    continue
  fi
  cd "$app"
  if ! dotnet build Smoke.csproj -c Release -warnaserror > "$log" 2>&1; then
    echo "FAIL $template (build)" >&2
    sed 's/^/     /' "$log" >&2
    failed=1
    cd "$repo"
    continue
  fi

  : > "$log"
  if [[ "$template" == tenantry-api ]]; then
    # Without this script's environment, so the launch profile sets it, as it does for a user.
    env -u ASPNETCORE_ENVIRONMENT -u DOTNET_ENVIRONMENT \
      dotnet run --no-build -c Release -- --urls "http://127.0.0.1:0" > "$log" 2>&1 &
    pid=$!
    url="" result=""
    if wait_for "$log" "Now listening on: http:"; then
      url=$(sed -n 's/.*Now listening on: \(http:[^ ]*\).*/\1/p' "$log" | head -n 1)
      token_for() { # <tenant>
        local tenant="$1"
        curl -s --max-time 30 -X POST "$url/dev/token" -H 'Content-Type: application/json' \
          -d "{\"subject\":\"smoke\",\"tenants\":[\"$tenant\"]}" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' || true
      }
      call() { # <method> <tenant> <token or empty> [body]; prints the status code, and the body to $work/body
        local method="$1" tenant="$2" token="$3" body="${4:-}" auth=()
        [[ -n "$token" ]] && auth=(-H "Authorization: Bearer $token")
        curl -s --max-time 30 -o "$work/body" -w '%{http_code}' -X "$method" -H "X-Tenant-Id: $tenant" \
          -H 'Content-Type: application/json' ${body:+-d "$body"} ${auth[@]+"${auth[@]}"} "$url/notes" || true
      }
      acme=$(token_for acme)
      globex=$(token_for globex)
      anonymous=$(call GET acme "")
      other=$(call GET globex "$acme")
      created=$(call POST acme "$acme" '{"text":"acme only"}')
      listed=$(call GET acme "$acme")
      grep -q "acme only" "$work/body" && acme_sees=yes || acme_sees=no
      call GET globex "$globex" > /dev/null
      grep -q "acme only" "$work/body" && globex_sees=yes || globex_sees=no
      result="anonymous $anonymous, acme's token for globex $other, create $created, list $listed, acme sees its note $acme_sees, globex sees it $globex_sees"
    fi
    stop
    if [[ "$result" == "anonymous 401, acme's token for globex 403, create 201, list 200, acme sees its note yes, globex sees it no" ]]; then
      echo "ok   $template ($result)"
    else
      echo "FAIL $template (${result:-${url:-did not start listening}})" >&2
      sed 's/^/     /' "$log" >&2
      failed=1
    fi
  else
    dotnet bin/Release/net10.0/Smoke.dll > "$log" 2>&1 &
    pid=$!
    status=0
    # Globex's message is the last: processed or failed, the worker has handled every message by then.
    wait_for "$log" "for tenant globex" || status=$?
    running=no
    kill -0 "$pid" 2>/dev/null && running=yes
    stop
    # The success lines in full, as the failure line also names the tenant.
    if [[ $status -eq 0 && $running == yes ]] && grep -q "Processed 'Acme's first order' for tenant acme" "$log" &&
       grep -q "Processed 'Globex's first order' for tenant globex" "$log" &&
       grep -q "Dropped a message for tenant initech" "$log" && grep -q "Dropped a message for tenant umbrella" "$log" &&
       grep -q "Failed to process a message for tenant acme" "$log"; then
      echo "ok   $template (processed acme's and globex's messages; dropped initech's and suspended umbrella's; logged the failing one and kept running)"
    else
      echo "FAIL $template ($([[ $status -eq 124 ]] && echo "no message for globex in ${deadline_seconds}s" || echo "did not process the messages as expected"))" >&2
      sed 's/^/     /' "$log" >&2
      failed=1
    fi
  fi
  cd "$repo"
done

exit $failed
