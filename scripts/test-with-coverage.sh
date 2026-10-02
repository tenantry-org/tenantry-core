#!/usr/bin/env bash
# Runs every test with code coverage, as CI does, after a Release build of the solution. dotnet-coverage
# (dotnet-tools.json) wraps `dotnet test` and writes a single report for all the test projects and target frameworks,
# coverage/coverage.xml, in the Visual Studio XML format that SonarCloud and ReportGenerator read. It instruments this
# repository's own assemblies, the src projects', where the test projects load them (eng/common/coverage.settings.xml).
# Arguments go to `dotnet test`. Usage:
#
#   scripts/test-with-coverage.sh [dotnet test options]
#
# Coverage is collected outside the test processes because an in-process extension's dependencies join the tests'
# own: coverlet.MTP needs Microsoft.Extensions 10 on every framework, above the .NET 8 and 9 floors the tests must
# resolve (scripts/check-dependency-floors.cs). Move to coverlet.MTP when .NET 8 and 9 are no longer supported.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
solutions=("$repo"/*.slnx)
if [[ ${#solutions[@]} -ne 1 || ! -f "${solutions[0]}" ]]; then
  echo "Expected one solution (*.slnx) in $repo" >&2
  exit 1
fi

# Every copy of a src assembly in a test project's Release output
instrument=()
for project in "$repo"/src/*/*.csproj; do
  for file in "$repo"/tests/*/bin/Release/*/"$(basename "$project" .csproj).dll"; do
    [[ -e "$file" ]] && instrument+=(--include-files "$file")
  done
done
if [[ ${#instrument[@]} -eq 0 ]]; then
  echo "No src assembly in tests/*/bin/Release: build the solution with -c Release first." >&2
  exit 1
fi

# From the repository root, whose global.json opts `dotnet test` into Microsoft Testing Platform
cd "$repo"
dotnet tool restore >/dev/null
dotnet dotnet-coverage collect --settings "$repo/eng/common/coverage.settings.xml" "${instrument[@]}" \
  --output-format xml --output "$repo/coverage/coverage.xml" \
  -- dotnet test --solution "${solutions[0]}" -c Release --no-build "$@"
