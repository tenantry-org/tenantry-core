#!/usr/bin/env bash
# Fails if any of the packages already has the version on the feed, so a release never publishes over, or skips, a
# version that exists: a moved or re-pushed tag would otherwise attest and release packages whose checksums do not
# match the published ones. Run by release.yml before anything is built. Usage:
#
#   scripts/check-unpublished.sh <version> <package id>...
#
# It reads each package's version list from the feed's flat container (NuGet's PackageBaseAddress resource):
# FLAT_CONTAINER, by default NuGet.org's https://api.nuget.org/v3-flatcontainer. For a feed that needs credentials,
# set FEED_USER and FEED_TOKEN, sent as basic authentication. An id the feed has never seen (404) has no versions; any
# other failure, including a response that is not JSON with a versions array, fails the check, since an unanswered
# question is not an answer.
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <version> <package id>..." >&2
  exit 2
fi

version="$1"
shift
feed="${FLAT_CONTAINER:-https://api.nuget.org/v3-flatcontainer}"
feed="${feed%/}"
credentials=()
if [[ -n "${FEED_USER:-}" && -n "${FEED_TOKEN:-}" ]]; then
  credentials=(--user "$FEED_USER:$FEED_TOKEN")
fi

# The flat container lists versions in lower case, normalised: no build metadata.
wanted="$(tr '[:upper:]' '[:lower:]' <<<"${version%%+*}")"
index="$(mktemp)"
trap 'rm -f "$index"' EXIT

published=()
for id in "$@"; do
  lower="$(tr '[:upper:]' '[:lower:]' <<<"$id")"
  status=0
  code="$(curl --proto '=https' --fail --silent --max-time 60 --retry 3 --output "$index" \
    --write-out '%{http_code}' ${credentials[@]+"${credentials[@]}"} "$feed/$lower/index.json")" || status=$?

  if [[ "$code" == 404 ]]; then
    echo "$id: not on the feed yet"
    continue
  fi
  if [[ $status -ne 0 ]]; then
    echo "::error::Could not read $id's versions from $feed (HTTP ${code:-none}, curl exit code $status)" >&2
    exit 1
  fi

  if ! jq -e '.versions | type == "array"' "$index" > /dev/null; then
    echo "::error::Could not read $id's versions from $feed: the response is not JSON with a versions array" >&2
    exit 1
  fi
  if jq -e --arg version "$wanted" '.versions | index($version) != null' "$index" > /dev/null; then
    published+=("$id")
    echo "$id: $version is already published"
  else
    echo "$id: $version is not published"
  fi
done

if [[ ${#published[@]} -gt 0 ]]; then
  echo "::error::$version is already on the feed for ${published[*]}. A published version cannot be replaced: release the next version." >&2
  exit 1
fi
