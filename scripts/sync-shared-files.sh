#!/usr/bin/env bash
# Copies the files Tenantry Pro shares with Tenantry Core (Core's eng/common/shared-files.txt) from a Core checkout
# into Pro, and deletes from Pro a file Pro's copy of the list names and Core's no longer does. Core keeps them: change
# one in Core, then run this in Pro. Usage, in Pro:
#
#   scripts/sync-shared-files.sh [<tenantry-core checkout>]           copy them (default ../tenantry-core)
#   scripts/sync-shared-files.sh --check [<tenantry-core checkout>]   copy nothing; fail if any differs (Pro's CI)
#
# The script is itself a shared file, so a copy overwrites it while it runs: the body is a function, which bash reads
# in full before it runs any of it.
set -euo pipefail

main() {

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

  # The paths each list names, read before the copy overwrites Pro's list. A file Core stops sharing is deleted here:
  # --check compares only the files Core lists, so a copy left behind would keep running in Pro unnoticed.
  listed() { awk '$1 !~ /^#/ && NF { print $1 }' "$1"; }
  shared="$(listed "$core/$manifest")"
  previous=""
  [[ -f "$repo/$manifest" ]] && previous="$(listed "$repo/$manifest")"

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

  if ! $check && [[ $different -eq 0 ]]; then
    while read -r path; do
      if [[ -n "$path" && -e "$repo/$path" ]] && ! grep -Fxq -- "$path" <<< "$shared"; then
        rm -f "$repo/$path"
        echo "Deleted $path: Tenantry Core no longer shares it. If Pro keeps its own copy, run git restore $path."
      fi
    done <<< "$previous"
  fi

  if [[ $different -ne 0 ]]; then
    $check && echo "Change shared files in Tenantry Core, then copy them here with scripts/sync-shared-files.sh." >&2
    exit 1
  fi
  $check && echo "The files shared with Tenantry Core match $core."
  exit 0
}

main "$@"
