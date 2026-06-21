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

## Checks your PR must pass

CI runs the same gates that block a release — make sure these hold locally before pushing:

1. **Build is warning-clean.** `TreatWarningsAsErrors` is on, including trim (`IL2xxx`) and AOT
   (`IL3xxx`) analyzer warnings for the `src/` projects. A warning fails the build.
2. **Tests pass on all target frameworks.**
3. **Line coverage ≥ 90%.** Add or update tests for any new code.
4. **SonarCloud quality gate.** Runs on pushes to `master` and internal PRs. (It is skipped on PRs
   from forks because secrets aren't available there — it runs after merge.)
5. **AOT publish succeeds** for the AOT sample (`dotnet publish samples/Tenantry.Samples.Aot -c Release`).

## Pull request flow

1. Fork the repo and create a topic branch.
2. Make your change with tests and docs.
3. Open a PR against `master` and fill in the PR template.
4. A maintainer reviews (CODEOWNERS are auto-requested). All conversations must be resolved and the
   required checks green before merge. History is linear — your PR will be squashed/rebased.

> **Note:** Workflows on PRs from forks require maintainer approval before they run.

## Commit & tag signing

Release tags (`v*`) are signed and protected. If you have signing configured, signed commits are
appreciated. See GitHub's guide on
[signing commits](https://docs.github.com/authentication/managing-commit-signature-verification/signing-commits).

## Releases (maintainers)

Releases are cut by pushing a signed `v*` tag. The release workflow re-runs the full CI gate
against the tagged commit, then **pauses for manual approval** (the `release` environment) before
publishing to NuGet.org via OIDC trusted publishing.
