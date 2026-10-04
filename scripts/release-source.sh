#!/usr/bin/env bash
# Prints the branches a release run's commit may be released from that contain it, one per line, or fails: master, or
# for a patch to an older minor its release/X.Y branch. Run by release.yml with GITHUB_REF_NAME (the tag, or the branch
# of a dry run) and GITHUB_SHA, in a checkout with every branch and tag fetched. Usage:
#
#   scripts/release-source.sh
#
# A tag vX.Y.Z may come from release/X.Y, never another minor's branch, and from master only while no tag, release
# candidates included, names a higher minor: once one does, master holds that minor's code, which must not ship as
# X.Y. A dry run may run on master or on a release branch itself.
set -euo pipefail

ref="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"
sha="${GITHUB_SHA:?GITHUB_SHA is not set}"

# A tag's major and minor as one number to compare: v0.7.0-rc.1 → 7, v1.2.3 → 1000002.
minor_of() {
  sed -nE 's/^v([0-9]+)\.([0-9]+)\..*/\1 \2/p' <<< "$1" | awk '{ printf "%d\n", $1 * 1000000 + $2 }'
}

case "$ref" in
  release/*)
    allowed=("$ref")
    ;;
  v*)
    own="$(minor_of "$ref")"
    if [[ -z "$own" ]]; then
      echo "::error::$ref is not a release tag (vX.Y.Z)" >&2
      exit 1
    fi

    allowed=("release/$(sed -nE 's/^v([0-9]+\.[0-9]+)\..*/\1/p' <<< "$ref")")
    highest="$(git tag --list 'v[0-9]*' | while read -r tag; do minor_of "$tag"; done | sort -n | tail -n 1)"
    if [[ -z "$highest" || "$highest" -le "$own" ]]; then
      allowed+=(master)
    fi
    ;;
  *)
    allowed=(master)
    ;;
esac

found=false
for branch in "${allowed[@]}"; do
  if git rev-parse --verify --quiet "refs/remotes/origin/$branch" > /dev/null &&
     git merge-base --is-ancestor "$sha" "refs/remotes/origin/$branch"; then
    echo "$branch"
    found=true
  fi
done

if [[ "$found" == false ]]; then
  echo "::error::$ref ($sha) is not on ${allowed[*]/#/origin/}; a release comes from master while no tag names a higher minor, or for a patch to an older minor from its release/X.Y branch" >&2
  exit 1
fi
