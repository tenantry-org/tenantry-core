#!/usr/bin/env bash
# Cuts a new minor's branch, release/X.Y, from master's head, and prepares both branches for its release, locally. On
# the branch, one commit names the CHANGELOG.md section (## [X.Y.0] - today, UTC) and moves the analyzer rules from
# each AnalyzerReleases.Unshipped.md to its AnalyzerReleases.Shipped.md. That commit is the one to tag: master gets a
# cherry-pick of it, a commit of its own, so the tag is never on master's history (scripts/release-source.sh), and a
# second commit raising MinVerMinimumMajorMinor to the next minor. It never pushes or tags: it prints the commands to
# do that (RELEASING.md). Usage, on master in a clean checkout:
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
cd "$(dirname "$0")/.."

fail() {
  echo "$1" >&2
  exit 1
}

[[ -z "$(git status --porcelain)" ]] || fail "The working tree has changes: commit or remove them first."
[[ "$(git symbolic-ref --quiet --short HEAD || true)" == master ]] || fail "Check out master first."
git fetch --quiet origin
[[ "$(git rev-parse master)" == "$(git rev-parse refs/remotes/origin/master)" ]] ||
  fail "master is not origin/master: pull or push master first."
! git rev-parse --verify --quiet "refs/heads/$branch" > /dev/null || fail "$branch already exists locally."
status=0
git ls-remote --exit-code --heads origin "$branch" > /dev/null || status=$?
[[ $status -eq 2 ]] || fail "$branch already exists on origin, or origin could not be read (git exit code $status)."
current="$(sed -n 's:.*<MinVerMinimumMajorMinor>\(.*\)</MinVerMinimumMajorMinor>.*:\1:p' Directory.Build.props)"
[[ "$current" == "$minor" ]] || fail "master works towards $current (MinVerMinimumMajorMinor), not $minor."
awk '$0 == "## [Unreleased]" { section = 1; next } section && /^## / { exit } section && NF { found = 1 }
  END { exit !found }' CHANGELOG.md || fail "CHANGELOG.md's ## [Unreleased] section is empty or missing."

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

title="Tenantry $version"
[[ "$minor" == 0.* ]] && title="$title (beta)"
cat <<EOF
Prepared $branch at $(git rev-parse --short "$release"), and two commits on master. Nothing is pushed or tagged.

1. Push the branch, and wait for CI, SonarCloud included, to pass on that push:
   git push origin $branch
2. Tag the branch's head and push the tag:
   git tag -a v$version -m "$title" origin/$branch
   git push origin v$version
3. Push master:
   git push origin master

To undo everything before pushing:
   git checkout master && git reset --hard origin/master && git branch -D $branch
EOF
