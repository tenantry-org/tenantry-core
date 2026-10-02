#!/usr/bin/env bash
# The latest dependency lane (run weekly by .github/workflows/dependency-lanes.yml): restores every version
# range at the newest release inside it (LatestDependencies=true, see eng/common/Build.targets), then builds
# and runs every test on every target framework. The committed lock files are left alone: the lane writes
# obj/latest.packages.lock.json, and copies them to artifacts/latest-dependencies as the record of what it ran.
# The other end of each range, its floor, is covered by CI (scripts/check-dependency-floors.cs).
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
solutions=("$repo"/*.slnx)
if [[ ${#solutions[@]} -ne 1 || ! -f "${solutions[0]}" ]]; then
  echo "Expected one solution (*.slnx) in $repo" >&2
  exit 1
fi
solution="${solutions[0]}"
lane=(-p:LatestDependencies=true -p:RestoreLockedMode=false -p:NuGetLockFilePath=obj/latest.packages.lock.json)

dotnet restore "$solution" "${lane[@]}"

record="$repo/artifacts/latest-dependencies"
rm -rf "$record" && mkdir -p "$record"
find "$repo/tests" "$repo/samples" -path '*/obj/latest.packages.lock.json' | while read -r lockfile; do
  project="$(basename "$(dirname "$(dirname "$lockfile")")")"
  cp "$lockfile" "$record/$project.packages.lock.json"
done

dotnet build "$solution" -c Release --no-restore "${lane[@]}"
# From the repository root, whose global.json opts `dotnet test` into Microsoft Testing Platform
cd "$repo"
dotnet test --solution "$solution" -c Release --no-build "${lane[@]}"
