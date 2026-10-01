#!/usr/bin/env bash
# Copies the files Tenantry Pro shares with Tenantry Core (Core's eng/common/shared-files.txt) from a Core checkout
# into Pro. Core keeps them: change one in Core, then run this in Pro. Usage, in Pro:
#
#   scripts/sync-shared-files.sh [<tenantry-core checkout>]           copy them (default ../tenantry-core)
#   scripts/sync-shared-files.sh --check [<tenantry-core checkout>]   copy nothing; fail if any differs (Pro's CI)
set -euo pipefail

check=false
if [[ "${1:-}" == "--check" ]]; then
  check=true
  shift
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
core="$(cd "${1:-$repo/../tenantry-core}" && pwd)"
manifest="eng/common/shared-files.txt"
if [[ "$core" == "$repo" ]]; then
  echo "Run this in Tenantry Pro, with a Tenantry Core checkout: Core is where the shared files are kept." >&2
  exit 2
fi
if [[ ! -f "$core/$manifest" ]]; then
  echo "$core has no $manifest" >&2
  exit 1
fi

different=0
while read -r path _ || [[ -n "${path:-}" ]]; do
  [[ -z "$path" || "$path" == \#* ]] && continue
  if [[ ! -f "$core/$path" ]]; then
    echo "$manifest lists $path, which Tenantry Core does not have" >&2
    different=1
  elif $check; then
    if ! cmp -s "$core/$path" "$repo/$path"; then
      echo "$path differs from Tenantry Core's" >&2
      different=1
    fi
  else
    mkdir -p "$(dirname "$repo/$path")"
    cp "$core/$path" "$repo/$path"
  fi
done < "$core/$manifest"

if [[ $different -ne 0 ]]; then
  $check && echo "Change shared files in Tenantry Core, then copy them here with scripts/sync-shared-files.sh." >&2
  exit 1
fi
$check && echo "The files shared with Tenantry Core match $core."
exit 0
