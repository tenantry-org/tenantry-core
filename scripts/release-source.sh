#!/usr/bin/env bash
# Prints the branch a release run's commit is released from, or fails. Every release tag vX.Y.Z, release candidates
# included, comes from its own branch release/X.Y, never from master or another minor's branch. A dry run may run on
# master or on a release branch itself. Run by release.yml with GITHUB_REF_NAME (the tag, or the branch of a dry run)
# and GITHUB_SHA, in a checkout with every branch fetched. Usage:
#
#   scripts/release-source.sh
set -euo pipefail

ref="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"
sha="${GITHUB_SHA:?GITHUB_SHA is not set}"

case "$ref" in
  v*)
    # vX.Y.Z or vX.Y.Z-prerelease as SemVer has them, with no leading zeros and no empty prerelease identifier:
    # v0.08.0, v0.6.2.1, v1, vfoo, v0.6.1-.. and v0.6.1-rc.01 are not release tags.
    identifier='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    if [[ ! "$ref" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-$identifier(\.$identifier)*)?$ ]]; then
      echo "::error::$ref is not a release tag: a release tag is vX.Y.Z, or vX.Y.Z-<prerelease> (v0.7.0-rc.1)" >&2
      exit 1
    fi
    branch="release/${BASH_REMATCH[1]}.${BASH_REMATCH[2]}"
    ;;
  *)
    branch="$ref"
    if [[ "$branch" != master && "$branch" != release/* ]]; then
      echo "::error::A dry run runs on master or on a release branch, not on $branch" >&2
      exit 1
    fi
    ;;
esac

if git rev-parse --verify --quiet "refs/remotes/origin/$branch" > /dev/null &&
   git merge-base --is-ancestor "$sha" "refs/remotes/origin/$branch"; then
  echo "$branch"
  exit 0
fi

echo "::error::$ref ($sha) is not on origin/$branch: a tag vX.Y.Z is released only from its branch release/X.Y" >&2
exit 1
