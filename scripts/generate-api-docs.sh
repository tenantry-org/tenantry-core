#!/usr/bin/env bash
# Generates the API reference in docs/api from the public API's XML documentation comments: docfx reads the
# src projects (net10.0) into metadata, and scripts/api-docs.cs writes one markdown page per public type. The
# pages are committed, so the docs at a release tag carry that release's reference; the site publishes them
# with the rest of the docs. Usage:
#
#   scripts/generate-api-docs.sh           rewrite docs/api
#   scripts/generate-api-docs.sh --check   fail if docs/api does not match the source (CI)
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
metadata="$repo/artifacts/api-metadata"
target="$repo/docs/api"
[[ "${1:-}" == "--check" ]] && target="$repo/artifacts/api-check"

# docfx (2.81) skips members declared in C# 14 extension blocks, so they would be missing from the reference.
if blocks="$(grep -rnE '^[[:space:]]*extension[[:space:]]*[<(]' "$repo/src" --include='*.cs')"; then
  echo "$blocks" >&2
  echo "The API reference cannot document C# 14 extension blocks: declare these as classic extension methods." >&2
  exit 1
fi

dotnet tool restore >/dev/null
rm -rf "$metadata"
dotnet docfx metadata "$repo/eng/api-docs/docfx.json" --output "$metadata" >/dev/null
dotnet run "$repo/scripts/api-docs.cs" -- "$metadata" "$target"

if [[ "${1:-}" == "--check" ]]; then
  if ! diff -r "$repo/docs/api" "$target" >/dev/null; then
    diff -ru "$repo/docs/api" "$target" | head -40 >&2 || true
    echo "docs/api is out of date: run scripts/generate-api-docs.sh and commit the result." >&2
    exit 1
  fi
  echo "docs/api matches the source."
fi
