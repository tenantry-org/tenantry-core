# Contributing to Tenantry

This guide covers the local workflow and the checks your change must pass.

## Prerequisites

The .NET 10 SDK builds every target framework (`net8.0;net9.0;net10.0`). To run the tests on all of them you also need
the 8.0 and 9.0 runtimes.

## Build & test

```bash
dotnet restore Tenantry.slnx
dotnet build   Tenantry.slnx -c Release
dotnet test    --solution Tenantry.slnx -c Release
```

The tests use xUnit.net v3 on Microsoft Testing Platform: `global.json` opts `dotnet test` into it, so it takes the
solution or a project as `--solution` or `--project`, and passes options it does not know on to the tests.
`tests/Tenantry.IntegrationTests` needs Docker: it runs against SQL Server, PostgreSQL and MySQL containers, one
per database for each target framework's run, one framework at a time (the image versions are in
`Providers/compose.yml`, which Dependabot updates within each major; `TENANTRY_SQLSERVER_IMAGE`,
`TENANTRY_POSTGRES_IMAGE` and `TENANTRY_MYSQL_IMAGE` run another image). Without Docker, run the other tests with

```bash
dotnet test --solution Tenantry.slnx -c Release --filter "Category!=Integration" --ignore-exit-code 8
```

Exit code 8 means a test application ran no tests: here, the integration tests the filter leaves out.

Package versions for `src/` and `tests/` are set centrally, in `Directory.Packages.props` and, for those Tenantry
Pro also uses, `eng/common/Packages.props`; each sample declares its own, as an application copied from it would.
Test and sample projects commit a `packages.lock.json`, and CI restores with `--locked-mode`, so it fails when a lock
file is stale: when you change a package version, run `dotnet restore Tenantry.slnx` and commit the lock files it
rewrites. `src/` projects have no lock files; the version ranges their packages declare are checked
by `scripts/check-package-ranges.cs` instead. In Rider, set Settings > Build, Execution, Deployment > NuGet > restore
engine to MSBuild: Rider's embedded engine writes the samples' lock files differently from the CLI, and CI's locked
restore then fails.

The build settings, scripts and package versions that `eng/common/shared-files.txt` lists are shared with the
Tenantry Pro repository, which copies them from here, so they hold nothing specific to this repository. CI also
builds on Windows.

## Checks your PR must pass

CI runs these steps (`.github/workflows/build-test.yml`) on every push to `master` or a `release/X.Y` branch and every
pull request into them, and the release workflow runs them again on the tagged commit. One job restores, builds, tests
and packs, in that order; a second job beside it runs the checks that need neither the tests nor the packages: the
workflow, script, Dependabot, name and link checks, the lock files, formatting, ReSharper's inspections, the API
reference and Native AOT. Each runs locally with the same command, from the repository root. `dotnet tool restore`
installs the tools `dotnet-tools.json` pins (docfx, dotnet-coverage, ReportGenerator, the Sonar scanner, CycloneDX and
ReSharper's command-line tools); the scripts that need them restore them too.

| Check | Command | When it fails |
|-------|---------|---------------|
| Every action is pinned to a commit | The `grep` in `build-test.yml` | Pin the action to a full commit SHA, with its version in a comment (`uses: owner/repo@<sha> # vX.Y.Z`) |
| The workflows are valid | `actionlint` (the `Lint the workflows` step in `build-test.yml`), which also runs shellcheck on each `run:` block | Fix what it reports |
| The scripts are valid | `shellcheck -S warning scripts/*.sh` | Fix what it reports, or disable that check on its line with a comment that says why |
| Dependabot's list of banded packages matches the project files | `dotnet run scripts/check-dependabot.cs` | Update the list in `.github/dependabot.yml` |
| No API or package that no longer exists is named in the README, docs, samples or `src/` | `dotnet run scripts/check-removed-names.cs` | Use the name it gives; the removed names are in `eng/common/removed-names.txt` |
| Every link reaches a file, page and heading | `dotnet run scripts/check-doc-links.cs` | Fix the link it names. Links in `docs/` are checked as tenantry.dev serves them: another page as `page.md#heading`, any other file through `../` |
| The lock files are up to date | `dotnet restore Tenantry.slnx --locked-mode` | `dotnet restore Tenantry.slnx`, then commit the lock files |
| Formatting | `dotnet format Tenantry.slnx --verify-no-changes --no-restore` | `dotnet format Tenantry.slnx` |
| Rider shows no warnings | `dotnet jb inspectcode Tenantry.slnx --severity=WARNING --swea --format=Sarif -o=artifacts/inspect.sarif`, which builds the solution first and writes each warning to `artifacts/inspect.sarif` | CI fails on any warning. Fix it, or suppress it with a comment that says why. Rider's settings are in `.editorconfig` |
| Every published floor is tested | `dotnet run scripts/check-dependency-floors.cs` | A version range's minimum must be a version some test project resolves |
| The build has no warnings | `dotnet build Tenantry.slnx -c Release --no-restore` | Warnings are errors, trim (`IL2xxx`) and AOT (`IL3xxx`) warnings in `src/` included |
| The samples start | `bash scripts/smoke-samples.sh` | After the Release build |
| Every test passes on every target framework, with coverage | `bash scripts/test-with-coverage.sh` | Docker runs the integration tests; it writes `coverage/coverage.xml` |
| SonarCloud quality gate | CI only | On every push to `master` and every pull request into it; not on `release/X.Y`, whose changes were analysed on `master` (the SonarCloud plan serves only the main branch). A pull request from a fork, or from Dependabot, gets no `SONAR_TOKEN`, so its Sonar step fails. For a result, a maintainer pushes its commits to a branch of this repository (`git fetch origin pull/<number>/head && git push origin FETCH_HEAD:refs/heads/<branch>`) and opens a pull request from that branch |
| Line coverage is at least 90% | `dotnet reportgenerator -reports:coverage/coverage.xml -targetdir:coverage/report -reporttypes:JsonSummary`, then `jq '.summary.linecoverage' coverage/report/Summary.json` | CI fails below 90: add tests for the new code |
| The public API still works for code built against the last release | `for p in src/*/*.csproj; do dotnet pack "$p" -c Release --no-build -o artifacts; done`, then `dotnet pack templates/Tenantry.Templates.csproj -c Release -o artifacts` for the templates package (the pack validates each library package against `TenantryPackageBaseline`) | Keep the old member, or, for an intended break in a minor release, record it with `dotnet pack -p:ApiCompatGenerateSuppressionFile=true` |
| A CycloneDX SBOM per package | The `dotnet CycloneDX` loop in `build-test.yml` | Run it after the pack, as it reads each package's version from `artifacts`, and fix the project it names |
| The packages' dependency ranges | `dotnet run scripts/check-package-ranges.cs -- artifacts` | Each dependency has its intended range (see the script) |
| The API reference is up to date | `bash scripts/generate-api-docs.sh --check` | `bash scripts/generate-api-docs.sh`, then commit `docs/api`: a change to the public API or its XML documentation changes it |
| An application can restore and run the packages | `rm -rf artifacts/libraries && mkdir artifacts/libraries && cp artifacts/*.nupkg artifacts/libraries/ && rm artifacts/libraries/Tenantry.Templates.[0-9]*.nupkg`, then `bash scripts/check-package-consumer.sh artifacts/libraries 'Tenantry.*'`: the packages without the templates package, which an application cannot reference | From an empty cache, with package source mapping, on every target framework |
| The templates work | `bash scripts/smoke-templates.sh artifacts` | Each `dotnet new` template is created from the templates package in `artifacts`, builds against the packages with warnings as errors, and runs |
| The docs' code blocks build | `bash scripts/check-doc-snippets.sh artifacts 'Tenantry.Core' 'Tenantry.*'` | Every `csharp` code block in the README and `docs/` builds against the packages |
| Native AOT publish | `dotnet publish samples/Tenantry.Samples.Aot -c Release` | No trim or AOT warnings |
| Native AOT smoke test | `dotnet publish eng/aot-smoke -c Release -o artifacts/aot-smoke`, then `artifacts/aot-smoke/AotSmoke` | Tenantry.Http and Tenantry.Caching compile whole for Native AOT, and the binary runs them |

When you remove or rename a public type, member, namespace or package, add the old name to
`eng/common/removed-names.txt`, with what replaces it, so the docs cannot keep it.

The release job, and on `master` the prerelease job, publish the packages CI built and checked, not a rebuild. CI also
restores and builds on Windows, and builds and tests for .NET 11 with its preview SDK (`ci.yml`,
`bash scripts/test-net11.sh`, after removing the SDK version from `global.json`). Every Monday, `dependency-lanes.yml`
runs the tests with every dependency at the newest version its range allows
(`bash scripts/test-latest-dependencies.sh`), and the integration tests against the newest database server releases.
A failure there opens an issue, or comments on the one still open.

## Pull request flow

1. Fork the repo and create a topic branch.
2. Make your change with tests and docs.
3. Open a PR against `master` and fill in the PR template.
4. A maintainer reviews (CODEOWNERS are requested automatically). All conversations must be resolved and the required
   checks green before merge. History is linear, so your PR is squashed or rebased.

Workflows on pull requests from forks run only once a maintainer approves them.

## Commit signing

Signed commits are welcome if you have signing set up
([GitHub's guide](https://docs.github.com/authentication/managing-commit-signature-verification/signing-commits)).

## Releases (maintainers)

Every release is a `vX.Y.Z` tag on the head of its own `release/X.Y` branch, published by the release workflow after
the maintainer approves it, and each push to `master` publishes a prerelease. [RELEASING.md](RELEASING.md) has the
steps, how to verify a package, and how to rehearse a release.
