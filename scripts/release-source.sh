#!/usr/bin/env bash
# Prints the branch a release run's commit is released from, or fails: master, or for a patch to an older minor its
# release/X.Y branch. Run by release.yml with GITHUB_REF_NAME (the tag, or the branch of a dry run) and GITHUB_SHA, in
# a checkout with every branch and tag fetched. Usage:
#
#   scripts/release-source.sh
#
# A tag vX.Y.Z may come from master or from release/X.Y, never another minor's branch. A tag below the newest release
# (a patch to an older minor once a newer one is out) may come only from release/X.Y: on master it would publish the
# newer minor's code under the older version. A dry run may run on master or on a release branch itself.
set -euo pipefail

ref="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"
sha="${GITHUB_SHA:?GITHUB_SHA is not set}"

case "$ref" in
  release/*)
    allowed=("$ref")
    ;;
  v*)
    release_branch="$(sed -nE 's/^v([0-9]+\.[0-9]+)\..*/release\/\1/p' <<< "$ref")"
    newest="$(git tag --list 'v[0-9]*' | { grep -v -- - || true; } | sort -V | tail -n 1)"
    older=false
    if [[ -n "$newest" && "$newest" != "$ref" && "$(printf '%s\n%s\n' "$ref" "$newest" | sort -V | tail -n 1)" == "$newest" ]]; then
      older=true
    fi

    allowed=()
    if [[ "$older" == false ]]; then
      allowed+=(master)
    fi
    if [[ -n "$release_branch" ]]; then
      allowed+=("$release_branch")
    fi
    ;;
  *)
    allowed=(master)
    ;;
esac

if [[ ${#allowed[@]} -eq 0 ]]; then
  echo "::error::$ref is not a release tag (vX.Y.Z), and is below the newest release, $newest" >&2
  exit 1
fi

for branch in "${allowed[@]}"; do
  if git rev-parse --verify --quiet "origin/$branch" > /dev/null && git merge-base --is-ancestor "$sha" "origin/$branch"; then
    echo "$branch"
    exit 0
  fi
done

echo "::error::$ref ($sha) is not on ${allowed[*]/#/origin/}; a release comes only from master, or for a patch to an older minor from its release/X.Y branch" >&2
exit 1
