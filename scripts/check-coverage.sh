#!/usr/bin/env bash
# Fails when the line coverage in coverage/coverage.xml, which scripts/test-with-coverage.sh writes, is below the
# percentage given. ReportGenerator (dotnet-tools.json) totals it into coverage/report/Summary.json. Usage:
#
#   scripts/check-coverage.sh <minimum line coverage percentage>
set -euo pipefail

if [[ $# -ne 1 || ! "$1" =~ ^[0-9]+(\.[0-9]+)?$ ]]; then
  echo "usage: $0 <minimum line coverage percentage>" >&2
  exit 2
fi

minimum="$1"

cd "$(dirname "$0")/.."
dotnet tool restore >/dev/null
dotnet reportgenerator -reports:coverage/coverage.xml -targetdir:coverage/report -reporttypes:JsonSummary
line="$(jq -e '.summary.linecoverage | numbers' coverage/report/Summary.json)"
echo "Total line coverage: ${line}%"
if ! awk -v line="$line" -v minimum="$minimum" 'BEGIN { exit !(line >= minimum) }'; then
  echo "::error::Line coverage ${line}% is below the required ${minimum}%"
  exit 1
fi
