# Contributing to Tenantry

Thanks for your interest in contributing! This guide covers the local workflow and the checks your
change must pass.

## Prerequisites

- .NET SDK **10.0** (the repo multi-targets `net8.0;net9.0;net10.0` — the 10 SDK builds all three).
  To *run* the full test matrix locally you also need the 8.0 and 9.0 runtimes installed.

## Build & test

```bash
dotnet restore Tenantry.slnx
dotnet build   Tenantry.slnx -c Release
dotnet test    --solution Tenantry.slnx -c Release
```

The tests use xUnit.net v3 on Microsoft Testing Platform: `global.json` opts `dotnet test` into it, so it takes the
solution or a project as `--solution` or `--project`, and passes options it does not know on to the tests.
`tests/Tenantry.IntegrationTests` needs **Docker**: it runs against SQL Server, PostgreSQL and MySQL containers, one
per database for each target framework's run, one framework at a time (the image versions are in
`Providers/ContainerImages.cs`). Without Docker, run the other tests with

```bash
dotnet test --solution Tenantry.slnx -c Release --filter "Category!=Integration" --ignore-exit-code 8
```

Exit code 8 means a test application ran no tests: here, the integration tests, which the filter leaves out.

Package versions for `src/` and `tests/` are set centrally, in `Directory.Packages.props` and, for those Tenantry
Pro also uses, `eng/common/Packages.props`; each sample declares its own, as an application copied from it would.
Test and sample projects commit a `packages.lock.json`, and CI restores with `--locked-mode`, so it fails
when a lock file is stale. When you change a package version, run `dotnet restore Tenantry.slnx` and commit the
lock files it rewrites. `src/` projects have no lock files; the version ranges their packages declare are checked
by `scripts/check-package-ranges.cs` instead.

The build settings, scripts and package versions that `eng/common/shared-files.txt` lists are shared with the
Tenantry Pro repository, which copies them from here, so they hold nothing specific to this repository. CI also
builds on Windows.

## Checks your PR must pass

CI runs these steps (`.github/workflows/build-test.yml`) on every push to `master` and every pull request, in this
order, and the release workflow runs them again on the tagged commit. Each one runs locally with the same command,
from the repository root. The scripts that need the tools in `dotnet-tools.json` (docfx, dotnet-coverage) restore
them, and the coverage gate needs ReportGenerator (`dotnet tool install -g dotnet-reportgenerator-globaltool`).

| Check | Command | When it fails |
|-------|---------|---------------|
| Dependabot's list of banded packages matches the project files | `dotnet run scripts/check-dependabot.cs` | Update the list in `.github/dependabot.yml` |
| No API or package that no longer exists is named in the README, docs, samples or `src/` | `dotnet run scripts/check-removed-names.cs` | Use the name it gives; the removed names are in `eng/common/removed-names.txt` |
| The lock files are up to date | `dotnet restore Tenantry.slnx --locked-mode` | `dotnet restore Tenantry.slnx`, then commit the lock files |
| Formatting | `dotnet format Tenantry.slnx --verify-no-changes --no-restore` | `dotnet format Tenantry.slnx` |
| Every published floor is tested | `dotnet run scripts/check-dependency-floors.cs` | A version range's minimum must be a version some test project resolves |
| The build has no warnings | `dotnet build Tenantry.slnx -c Release --no-restore` | Warnings are errors, trim (`IL2xxx`) and AOT (`IL3xxx`) warnings in `src/` included |
| The samples start | `bash scripts/smoke-samples.sh` | After the Release build |
| Every test passes on every target framework, with coverage | `bash scripts/test-with-coverage.sh` | Docker runs the integration tests; it writes `coverage/coverage.xml` |
| SonarCloud quality gate | CI only | On pushes to `master` and every pull request. A pull request from a fork gets no secrets, so its Sonar step fails for lack of the token |
| Line coverage is at least 90% | `reportgenerator -reports:coverage/coverage.xml -targetdir:coverage/report -reporttypes:JsonSummary`, then `jq '.summary.linecoverage' coverage/report/Summary.json` | CI fails below 90: add tests for the new code |
| The public API still works for code built against the last release | Part of `dotnet pack` (package validation against `TenantryPackageBaseline`) | Keep the old member, or, for an intended break in a minor release, record it with `dotnet pack -p:ApiCompatGenerateSuppressionFile=true` |
| The packages' dependency ranges | `for p in src/*/*.csproj; do dotnet pack "$p" -c Release --no-build -o artifacts; done`, then `dotnet run scripts/check-package-ranges.cs -- artifacts` | Each dependency has its intended range (see the script) |
| The API reference is up to date | `bash scripts/generate-api-docs.sh --check` | `bash scripts/generate-api-docs.sh`, then commit `docs/api`: a change to the public API or its XML documentation changes it |
| An application can restore and run the packages | `bash scripts/check-package-consumer.sh artifacts 'Tenantry.Core' 'Tenantry.EfCore' 'Tenantry.AspNetCore'` | From an empty cache, with package source mapping, on every target framework |
| The docs' code blocks build | `bash scripts/check-doc-snippets.sh artifacts 'Tenantry.Core' 'Tenantry.EfCore' 'Tenantry.AspNetCore'` | Every `csharp` code block in the README and `docs/` builds against the packages |
| Native AOT publish | `dotnet publish samples/Tenantry.Samples.Aot -c Release` | No trim or AOT warnings |

When you remove or rename a public type, member, namespace or package, add the old name to
`eng/common/removed-names.txt`, with what replaces it, so the docs cannot keep it.

The release job publishes the packages CI built and checked, not a rebuild. CI also restores and builds on Windows,
and builds and tests for .NET 11 with its preview SDK (`ci.yml`, `bash scripts/test-net11.sh`, after removing the SDK
version from `global.json`). Every Monday, `dependency-lanes.yml` runs the tests with every dependency at the newest
version its range allows (`bash scripts/test-latest-dependencies.sh`).

## Pull request flow

1. Fork the repo and create a topic branch.
2. Make your change with tests and docs.
3. Open a PR against `master` and fill in the PR template.
4. A maintainer reviews (CODEOWNERS are auto-requested). All conversations must be resolved and the
   required checks green before merge. History is linear — your PR will be squashed/rebased.

> **Note:** Workflows on PRs from forks require maintainer approval before they run.

## Commit signing

If you have signing configured, signed commits are appreciated. See GitHub's guide on
[signing commits](https://docs.github.com/authentication/managing-commit-signature-verification/signing-commits).

## Releases (maintainers)

Releases are cut by pushing a `v*` tag on a commit that is on `master`; a ruleset lets only the
maintainer create, move or delete `v*` tags. The release workflow checks that the tag is on `master`,
reruns the CI gate on the tagged commit (without SonarCloud, which already passed on `master`),
including both package checks, then **pauses for approval** in the `release` environment (only `v*`
tags can deploy to it) and publishes those same packages, with their symbol packages, to NuGet.org via
OIDC trusted publishing. The GitHub release's notes are the version's section of `CHANGELOG.md`
(`scripts/release-notes.sh`), and a tag without one fails before anything is built; nothing is attached.
[RELEASING.md](RELEASING.md) has the steps.

To rehearse a release, run the Release workflow manually (Actions → Release → Run workflow) on
`master`: it runs the same checks and builds the same packages, then lists what a release would
publish, without publishing anything. Give it the tag to also see that release's notes.
