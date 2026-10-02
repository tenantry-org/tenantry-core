#!/usr/bin/env bash
# Prints a release's notes, its section of CHANGELOG.md: the lines under its "## [x.y.z]" heading, up to the next
# "## " heading. The release workflow publishes them as the GitHub release's notes, and fails before publishing
# anything when the section is missing or empty. Usage:
#
#   scripts/release-notes.sh <tag> [<changelog>]      e.g. scripts/release-notes.sh v0.5.0
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 <tag> [<changelog>]" >&2
  exit 2
fi

version="${1#v}"
changelog="${2:-$(cd "$(dirname "$0")/.." && pwd)/CHANGELOG.md}"

# The section without its heading, and without blank lines at either end.
notes="$(awk -v heading="## [$version]" '
  index($0, heading) == 1 { found = 1; next }
  found && /^## / { exit }
  found && /^[[:space:]]*$/ { if (started) blank++; next }
  found { while (blank > 0) { print ""; blank-- } started = 1; print }
' "$changelog")"

if [[ -z "$notes" ]]; then
  echo "$changelog has no notes for $version: add them under a \"## [$version] - <date>\" heading." >&2
  exit 1
fi
printf '%s\n' "$notes"
