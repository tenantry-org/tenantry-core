#!/usr/bin/env bash
# Prints the newest release among the tag names on standard input: the highest release version by version sort, or
# nothing when there is none. Release versions only (vX.Y.Z, no prerelease part, no leading zeros), so a prerelease or a
# stray tag is never the newest. Only the newest release is GitHub's Latest (release.yml): a patch to an older minor, or
# a prerelease, does not take that from a newer release. release.yml decides when it creates the release, so a rerun
# after a newer release is tagged does not either. Usage:
#
#   git tag --list | scripts/newest-release.sh
set -euo pipefail

release='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
{ grep -E "$release" || true; } | sort -V | tail -n 1
