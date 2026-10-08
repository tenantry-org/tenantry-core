#!/usr/bin/env bash
# Cuts a new minor's branch, release/X.Y, from master's head, and prepares both branches for its release, locally. On
# the branch, one commit names the CHANGELOG.md section (## [X.Y.0] - today, UTC) and moves the analyzer rules from
# each AnalyzerReleases.Unshipped.md to its AnalyzerReleases.Shipped.md. That commit is the one to tag: master gets a
# cherry-pick of it, a commit of its own, so the tag is never on master's history (scripts/release-source.sh), and a
# second commit raising MinVerMinimumMajorMinor to the next minor. It always raises the minor: when the next release is
# a major, edit MinVerMinimumMajorMinor in that commit by hand before pushing. It never pushes or tags: it prints the
# commands to do that (RELEASING.md). Usage, on master in a clean checkout, with gh signed in:
#
#   scripts/cut-release.sh <X.Y>      e.g. scripts/cut-release.sh 0.7
set -euo pipefail

if [[ $# -ne 1 || ! "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "usage: $0 <X.Y>, the minor to release, e.g. 0.7" >&2
  exit 2
fi

minor="$1"
version="$minor.0"
next="${BASH_REMATCH[1]}.$((BASH_REMATCH[2] + 1))"
branch="release/$minor"
undo="git checkout --force master && git reset --hard origin/master && git branch -D $branch"
cd "$(dirname "$0")/.."

fail() {
  echo "$1" >&2
  exit 1
}

# Exit code 2 from git ls-remote --exit-code: origin has no such ref.
absent_on_origin() {
  local ref="$1" status=0
  git ls-remote --exit-code origin "$ref" > /dev/null || status=$?
  [[ $status -eq 2 ]]
}

[[ -z "$(git status --porcelain)" ]] || fail "The working tree has changes: commit or remove them first."
[[ "$(git symbolic-ref --quiet --short HEAD || true)" == master ]] || fail "Check out master first."
git fetch --quiet origin
[[ "$(git rev-parse master)" == "$(git rev-parse refs/remotes/origin/master)" ]] ||
  fail "master is not origin/master: pull or push master first."
# SonarCloud analyses master only, so the branch is cut from a commit whose CI run, quality gate included, passed.
command -v gh > /dev/null || fail "gh, the GitHub CLI, is needed to check master's CI: install it, then gh auth login."
head="$(git rev-parse master)"
passed="$(gh run list --repo "$(git remote get-url origin)" --workflow ci.yml --branch master --event push \
  --commit "$head" --status success --json databaseId --jq 'length')" ||
  fail "gh could not read master's CI runs (above): origin must be a GitHub URL, and gh signed in (gh auth login)."
[[ "$passed" != 0 ]] || fail "CI has not passed on master's head, $head: wait for its push's run, or fix master first."
! git rev-parse --verify --quiet "refs/heads/$branch" > /dev/null || fail "$branch already exists locally."
absent_on_origin "refs/heads/$branch" || fail "$branch already exists on origin, or origin could not be read."
! git rev-parse --verify --quiet "refs/tags/v$version" > /dev/null || fail "The tag v$version already exists locally."
absent_on_origin "refs/tags/v$version" ||
  fail "The tag v$version already exists on origin, or origin could not be read."
current="$(sed -n 's:.*<MinVerMinimumMajorMinor>\(.*\)</MinVerMinimumMajorMinor>.*:\1:p' Directory.Build.props)"
[[ "$current" == "$minor" ]] || fail "master works towards $current (MinVerMinimumMajorMinor), not $minor."
! grep -q "^## \[$version\]" CHANGELOG.md || fail "CHANGELOG.md already has a ## [$version] section."
! grep -qxF "## Release $version" analyzers/*/AnalyzerReleases.Shipped.md ||
  fail "An AnalyzerReleases.Shipped.md already has a ## Release $version section."
# An entry is a line in the section that is neither blank nor a heading.
awk '$0 == "## [Unreleased]" { section = 1; next } section && /^## / { exit } section && NF && !/^#/ { found = 1 }
  END { exit !found }' CHANGELOG.md || fail "CHANGELOG.md's ## [Unreleased] section has no entries, or is missing."

trap 'echo "cut-release.sh stopped part way. To undo what it did: $undo" >&2' ERR

git checkout --quiet -b "$branch"

# The section keeps its entries under its version; a new, empty ## [Unreleased] goes above it.
awk -v heading="## [$version] - $(date -u +%Y-%m-%d)" '
  !done && $0 == "## [Unreleased]" { print; print ""; print heading; done = 1; next }
  { print }
' CHANGELOG.md > CHANGELOG.md.new
mv CHANGELOG.md.new CHANGELOG.md

# Each Unshipped file keeps its ; comment lines; what follows them goes to the end of Shipped, under the release.
for unshipped in analyzers/*/AnalyzerReleases.Unshipped.md; do
  shipped="$(dirname "$unshipped")/AnalyzerReleases.Shipped.md"
  rules="$(sed '/^;/d' "$unshipped" | sed '/./,$!d')"
  [[ -n "$rules" ]] || continue
  printf '%s\n\n## Release %s\n\n%s\n' "$(< "$shipped")" "$version" "$rules" > "$shipped"
  printf '%s\n\n' "$(sed -n '/^;/p' "$unshipped")" > "$unshipped"
done

git add CHANGELOG.md analyzers
git commit --quiet -m "Releasing: $version's CHANGELOG.md section and shipped analyzer rules"
release="$(git rev-parse HEAD)"

git checkout --quiet master
# -x names the branch's commit in the message, which also keeps the two commits apart: one made in the same second
# from the same parent and tree would otherwise be the same commit, putting the tagged commit on master.
git cherry-pick -x "$release" > /dev/null
property=MinVerMinimumMajorMinor
sed "s:<$property>$minor</$property>:<$property>$next</$property>:" Directory.Build.props > Directory.Build.props.new
mv Directory.Build.props.new Directory.Build.props
git commit --quiet -am "Releasing: master works towards $next (MinVerMinimumMajorMinor)"

if [[ "$(git rev-parse master~1)" == "$release" ]] || git merge-base --is-ancestor "$release" master; then
  fail "$branch's commit is on master's history, so it cannot be tagged. To undo what this did: $undo"
fi
trap - ERR

# The key the tag command names: git's configured signing key when it is an SSH key (gpg.format is ssh, or the key is a
# path or a key:: literal; --type=path expands a leading ~), or else a placeholder to replace.
key="$(git config --get --type=path user.signingkey || true)"
if [[ "$(git config --get gpg.format || true)" != ssh && "$key" != */* && "$key" != key::* ]]; then
  key=""
fi
key="${key:-<your signing key, such as ~/.ssh/id_ed25519.pub>}"
title="Tenantry $version"
[[ "$minor" == 0.* ]] && title="$title (beta)"
cat <<EOF
Prepared $branch at $(git rev-parse --short "$release"), and two commits on master. Nothing is pushed or tagged.

1. Push the branch, and wait for CI to pass on that push:
   git push origin $branch
2. Push master:
   git push origin master
3. Tag the branch's head, signed with your signing key's public key file, and push the tag:
   git -c gpg.format=ssh -c "user.signingkey=$key" \\
     tag -s v$version -m "$title" origin/$branch
   git push origin v$version

To undo everything before pushing:
   $undo
EOF
