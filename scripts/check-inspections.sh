#!/usr/bin/env bash
# Fails on any ReSharper warning: Rider's inspections, with its solution-wide analysis and the settings in
# .editorconfig, as CI runs them. inspectcode writes each warning to artifacts/inspect.sarif, and this prints each one
# as a GitHub error annotation before failing. Fix a warning, or suppress it with a comment that says why. Options
# after the solution go to inspectcode: Tenantry Core lets it build the solution first, and Tenantry Pro passes
# --no-build after its own build. The solution's path is relative to the repository root. Usage:
#
#   scripts/check-inspections.sh <solution> [inspectcode options]
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <solution> [inspectcode options]" >&2
  exit 2
fi

solution="$1"
shift
sarif=artifacts/inspect.sarif

cd "$(dirname "$0")/.."
dotnet tool restore >/dev/null
dotnet jb inspectcode "$solution" --severity=WARNING --swea --format=Sarif -o="$sarif" --verbosity=WARN "$@"

jq -r '.runs[].results[] | .locations[0].physicalLocation as $at
  | "::error file=\($at.artifactLocation.uri // ""),line=\($at.region.startLine // 1)::"
    + "\(.ruleId): \(.message.text)"' "$sarif"
count="$(jq '[.runs[].results[]] | length' "$sarif")"
if [[ "$count" != "0" ]]; then
  echo "::error::ReSharper reports $count warnings (above): Rider must show none"
  exit 1
fi
