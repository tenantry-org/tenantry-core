#!/usr/bin/env bash
# The .NET 11 preview lane: restores the solution with -p:IncludeNet11=true, which adds net11.0 to the src
# and test projects, then builds and runs every test project for net11.0 against the .NET 11 release
# candidate. The integration tests cover SQL Server and PostgreSQL there (no MySQL provider exists for
# EF Core 11 yet). Needs a .NET 11 SDK: global.json pins 10.0, so the CI job removes it first.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"

sdk="$(cd "$repo" && dotnet --version)"
if [[ "$sdk" != 11.* ]]; then
  echo "The .NET 11 lane needs a .NET 11 SDK, but $repo selects $sdk (global.json pins 10.0)." >&2
  exit 1
fi

# The lane's package graph includes net11.0, so write its lock files under obj/ and leave the committed
# ones untouched.
dotnet restore "$repo/Tenantry.slnx" -p:IncludeNet11=true -p:NuGetLockFilePath=obj/net11.packages.lock.json

for project in "$repo"/tests/*/*.csproj; do
  dotnet test "$project" -c Release -f net11.0 --no-restore -p:IncludeNet11=true \
    --logger "console;verbosity=minimal"
done
