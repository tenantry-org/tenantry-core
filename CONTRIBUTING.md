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
dotnet test    Tenantry.slnx -c Release
```

The tests use xUnit.net v3 and run through VSTest. `tests/Tenantry.IntegrationTests` needs **Docker**: it runs
against SQL Server, PostgreSQL and MySQL containers, one per database for each target framework's run, one framework
at a time (the image versions are in `Providers/ContainerImages.cs`). Without Docker, run the other tests with

```bash
dotnet test Tenantry.slnx -c Release --filter "Category!=Integration"
```

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

CI runs the same gates that block a release — make sure these hold locally before pushing:

1. **Build is warning-clean.** `TreatWarningsAsErrors` is on, including trim (`IL2xxx`) and AOT
   (`IL3xxx`) analyzer warnings for the `src/` projects. A warning fails the build.
2. **Tests pass on all target frameworks.**
3. **Line coverage ≥ 90%.** Add or update tests for any new code.
4. **SonarCloud quality gate.** Runs on pushes to `master` and internal PRs. (It is skipped on PRs
   from forks because secrets aren't available there — it runs after merge.)
5. **AOT publish succeeds** for the AOT sample (`dotnet publish samples/Tenantry.Samples.Aot -c Release`).
6. **The packages pass both package checks**, which CI runs after packing the `src/` projects.
   `dotnet run scripts/check-package-ranges.cs -- artifacts --siblings minor` checks that every dependency has its
   intended range, and
   `scripts/check-package-consumer.sh artifacts 'Tenantry.Core' 'Tenantry.EfCore' 'Tenantry.AspNetCore'`
   has a stand-in application restore them from an empty cache with package source mapping, build for
   every target framework and run. The release job publishes the packages CI built and checked, not a
   rebuild.

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
OIDC trusted publishing. The GitHub release gets generated notes and no attached files.

To rehearse a release, run the Release workflow manually (Actions → Release → Run workflow) on
`master`: it runs the same checks and builds the same packages, then lists what a release would
publish, without publishing anything.
