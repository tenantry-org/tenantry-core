# Releasing Tenantry

Every release is a `vX.Y.Z` tag, release candidates (`vX.Y.Z-rc.1`) included, on the head of its own branch,
`release/X.Y`, on a commit of the branch's own; never on a commit `master` contains, or on another minor's branch.
`master` never releases a stable version: each push to it publishes a prerelease instead
([Prereleases from master](#prereleases-from-master)). Changes land on `master`, and a release branch takes only what
its releases need, cherry-picked from `master`. The release workflow (`.github/workflows/release.yml`) does the rest.
Only the maintainer can push `v*` tags.

## A new minor

1. Get `## [Unreleased]` in `CHANGELOG.md` ready, in a pull request into `master` as usual: it becomes the GitHub
   release's notes, and starts with the steps to update from the previous minor.
2. In the checkout where `master` is checked out, up to date with `origin/master` and with nothing uncommitted, cut
   the branch:

   ```sh
   scripts/cut-release.sh 0.7
   ```

   It refuses unless `0.7` is `MinVerMinimumMajorMinor`, CI passed on the push of `master`'s head (SonarCloud's quality
   gate included, which release branches and tags skip; read with `gh`, signed in), `release/0.7` and `v0.7.0` exist
   neither locally nor on `origin`, `## [Unreleased]` has entries, and no `## [0.7.0]` or `## Release 0.7.0` section
   exists yet. It creates `release/0.7` from `master`'s head, with one commit of its own, the one to tag:
   `## [Unreleased]` becomes `## [0.7.0] - YYYY-MM-DD` (today, UTC) below a new, empty `## [Unreleased]`, and each
   analyzer rule moves from `analyzers/*/AnalyzerReleases.Unshipped.md` to that folder's `AnalyzerReleases.Shipped.md`,
   under `## Release 0.7.0`. On `master`, it cherry-picks that commit, then raises `MinVerMinimumMajorMinor` to `0.8`
   in a second commit, so `master`'s prereleases are versioned above the branch's releases. It always raises the minor:
   when the next release is a major, edit `MinVerMinimumMajorMinor` in that second commit by hand before pushing. It
   pushes and tags nothing, and prints the commands for the steps below and how to undo it.

   The branch's commit reaches `master` only as a cherry-pick, a new commit with its own hash, never by merging or
   fast-forwarding the branch into `master`: that would put the tagged commit on `master`'s history, where MinVer
   would count `master`'s prerelease versions from the tag and restart them. The release workflow refuses a tag on a
   commit `master` contains, and the prerelease job refuses a version below one already published.
3. Push the branch, and wait for CI to pass on that push: the release requires that run.

   ```sh
   git push origin release/0.7
   ```

4. Push `master`, before the tag, so the release workflow checks the tag against `master` with the cherry-pick on it:

   ```sh
   git push origin master
   ```

5. Rehearse (optional): Actions → Release → Run workflow on `release/X.Y`, with the tag as the version. It runs the
   release's checks, builds the same packages, and shows what a release would publish and its notes.
6. Tag the branch's head, signed, and push the tag. The script prints the command with git's configured signing key
   (`git config user.signingkey`) when it is an SSH key; without one, or with a GPG key id, name your SSH public key
   file in place of the example:

   ```sh
   git -c gpg.format=ssh -c user.signingkey=~/.ssh/id_ed25519.pub \
     tag -s v0.7.0 -m "Tenantry 0.7.0 (beta)" origin/release/0.7
   git push origin v0.7.0
   ```

The script makes only a minor's `X.Y.0`. To start with a release candidate, do step 2 by hand:

1. From `master`, up to date: `git switch -c release/0.7`.
2. In `CHANGELOG.md`, add `## [0.7.0-rc.1] - YYYY-MM-DD`, dated today in UTC, below `## [Unreleased]`, so the
   section holds the entries, and leave `## [Unreleased]` empty above it.
3. Move the analyzer rules to `AnalyzerReleases.Shipped.md` under `## Release 0.7.0`, not `## Release 0.7.0-rc.1`:
   the release-tracking analyzer takes only a numeric version, and fails the build (RS2007) on a prerelease one.
4. Commit, then `git switch master` and `git cherry-pick -x release/0.7`, and raise `MinVerMinimumMajorMinor` in a
   second commit.

Later candidates and `v0.7.0` follow on the same branch, each from a pull request into it that adds its own
`CHANGELOG.md` section (`## [0.7.0-rc.2]`, `## [0.7.0]`), as a patch does. A rule added after the first candidate
joins the `## Release 0.7.0` table, which the analyzer accepts, rather than a second section. Every section added to
`release/X.Y` is also added to `master`'s `CHANGELOG.md`, placed by version, before the tag is pushed: the release
workflow refuses a tag whose section is missing from `origin/master`. The first candidate's section reaches `master` as
the cherry-pick in step 4 above, so push `master` before its tag, as for a new minor.

## A patch

1. Fix it on `master` first, in a pull request as usual, unless the code is gone there. Before cherry-picking the fix
   to the release branch, check that `master`'s CI, SonarCloud included, has passed on it: release branches skip
   SonarCloud.
2. A tag runs the `release.yml` of the tagged commit. `release/0.7` was cut before `release.yml` checked a patch's
   `TenantryPackageBaseline` and its changelog section on `master`, and before it attached the packages, so the first
   0.7 patch brings them first, in a pull request into `release/0.7`: `.github/workflows/release.yml` from `master`,
   `TenantryPackageBaseline` raised to `0.7.0`, and both `src/*/CompatibilitySuppressions.xml` deleted. Branches cut
   from `master` from 0.8 on have them already.
3. In a pull request into `release/X.Y`, which runs CI, cherry-pick the fix and add the patch's
   section to `CHANGELOG.md` (`## [0.7.1] - YYYY-MM-DD`). Add the same section to `master`'s `CHANGELOG.md`, placed by
   version among the other releases. The release's notes come from the branch's copy.
4. Merge it and wait for CI to pass on the push to `release/X.Y`. Then run the Dependency lanes workflow on
   `release/X.Y` (Actions → Dependency lanes → Run workflow) and wait for it to pass: its weekly run tests only
   `master`.
5. Before tagging, check that the tag is the line's next patch (`git tag --list 'v0.7.*'`), that `git log
   v0.7.0..origin/release/0.7` holds only what the patch should, that `TenantryPackageBaseline` names the line's last
   release, and that the `CHANGELOG.md` section is there, and on `master`. With the `release.yml` of step 2, the
   workflow checks the section on both branches, and refuses a patch whose `TenantryPackageBaseline` is not a release
   of its own minor. Then tag the branch's head and push the tag, as for a new minor.

Tag the head of a push to `release/X.Y`: CI runs once for each push, on its last commit, so a commit in the middle of
a push of several has no run of its own, and its tag is refused.

A minor older than the latest gets only security fixes ([security policy](.github/SECURITY.md#supported-versions)):
its patch's section has a `### Security` heading, and its security advisory is published once the packages are on
NuGet.org.

## The release

1. The workflow checks that the tag is a release tag on its branch's history, that CI passed on the push to
   `release/X.Y` that put the commit there, and that it has notes, on its branch and, with `master`'s `release.yml`
   ([A patch](#a-patch), step 2), on `master`. A tag on a commit `master` contains, on another minor's branch, or on an
   unmerged branch fails before anything is built (`scripts/release-source.sh`).
2. It reruns CI on the tagged commit, then waits for approval in the `release` environment. Once approved, it pushes
   the six library packages and their symbol packages to NuGet.org, then `Tenantry.Templates`
   ([The templates package](#the-templates-package)), and creates the GitHub release, marked as the latest only if no
   higher version is released.
3. NuGet.org validates and indexes the packages, which takes a few minutes. They are available once
   `https://api.nuget.org/v3-flatcontainer/tenantry.core/index.json` lists the version. A version can be
   unlisted afterwards, but never deleted or replaced.

The GitHub release attaches the packages as the workflow built them, which its checksums (`SHA256SUMS`) and build
provenance attestations are for. NuGet.org adds its repository signature to each package it serves, so its copy
hashes differently and has no attestation of its own. A tag runs the `release.yml` of the tagged commit, so only a
release branch whose `release.yml` attaches the packages gives releases that do: those cut from `master` from 0.8 on,
and `release/0.7` once a patch brings `master`'s `release.yml` to it ([A patch](#a-patch)). 0.7.0 and earlier releases
attach only the checksums and SBOMs. To check a package, download each copy into its own folder, since the two file
names differ only in case:

```sh
mkdir github nuget
gh release download v0.8.0 --repo tenantry-org/tenantry-core -p Tenantry.Core.0.8.0.nupkg -D github
curl -fsSL -o nuget/tenantry.core.0.8.0.nupkg \
  https://api.nuget.org/v3-flatcontainer/tenantry.core/0.8.0/tenantry.core.0.8.0.nupkg
gh attestation verify github/Tenantry.Core.0.8.0.nupkg --repo tenantry-org/tenantry-core
openssl dgst -sha512 -binary github/Tenantry.Core.0.8.0.nupkg | openssl base64 -A && echo
dotnet nuget verify --all nuget/tenantry.core.0.8.0.nupkg
```

`gh attestation verify` shows the attached package was built by the release workflow from the tagged commit.
`dotnet nuget verify --all` checks NuGet.org's signature on its copy and prints its content hash, the SHA-512 of the
package before NuGet.org signed it, which is the line `openssl` prints.

Before anything is built, the workflow checks that no package already has the version on NuGet.org
(`scripts/check-unpublished.sh`), and the push refuses a duplicate rather than skipping it. So a tag moved or pushed
again for a released version fails, instead of creating a GitHub release whose checksums do not match the published
packages. A dry run given a version checks it too.

If a release stops part way, after some packages were pushed, rerunning it does not finish it, and a published package
cannot be replaced. Rerunning the failed job fails at the push, as NuGet.org refuses a version a package has already;
rerunning every job fails at the check that the version is unpublished. Release the next patch instead, from the same
branch: add its `CHANGELOG.md` section, which says it replaces the incomplete release, and tag it. Unlist the
incomplete version's packages on NuGet.org, so no one picks a set that does not match, unless the release stopped
only at `Tenantry.Templates` ([The templates package](#the-templates-package)). `TenantryPackageBaseline` names the
last release whose six library packages were all published, which is the incomplete one only in that case: pack looks
for each library package at the baseline version, and would fail for one never published at it. An incomplete `X.Y.0`
is the exception, as the workflow refuses a patch whose baseline is in an older minor: set `TenantryPackageBaseline` to
`X.Y.0`, and in the project file of each library package `X.Y.0` lacks, to the previous release.

If every package was pushed and only the GitHub release is missing, create it by hand instead, from the run's packages,
which it attested before the push, within 7 days of the run, while GitHub keeps them. In a checkout with the tags
fetched, with the run's id from its URL, and the same files and marks the workflow would have given it:

```sh
tag=v0.7.1
run=<the release run's id>
rm -rf artifacts/release
gh run download "$run" --repo tenantry-org/tenantry-core --name nuget-packages --dir artifacts/release
(cd artifacts/release && sha256sum -- *.nupkg *.snupkg > SHA256SUMS)
bash scripts/release-notes.sh "$tag" <(git show "$tag:CHANGELOG.md") > artifacts/release/notes.md
release='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
newest="$(git tag --list 'v[0-9]*' | grep -E "$release" | sort -V | tail -n 1)"
flags=(--latest=false)
[[ "$tag" == "$newest" ]] && flags=(--latest)
[[ "$tag" == *-* ]] && flags+=(--prerelease)
gh release create "$tag" --repo tenantry-org/tenantry-core --verify-tag --title "$tag" \
  --notes-file artifacts/release/notes.md "${flags[@]}" artifacts/release/*.nupkg artifacts/release/*.snupkg \
  artifacts/release/SHA256SUMS artifacts/release/sbom/*.cdx.json
```

If the workflow created the release but not all its files, upload the missing ones with `gh release upload "$tag"
<files> --clobber` instead.

## Prereleases from master

Each push to `master` publishes its packages to NuGet.org as a prerelease, with no approval, once that run's Build &
Test job and Windows build have passed; the .NET 11 lane is not waited for. The `prerelease` job in
`.github/workflows/ci.yml` pushes the packages, with their symbol packages, that the Build & Test job built and checked.
A version is `X.Y.0-alpha.0.N`, such as `0.7.0-alpha.0.126`. `X.Y` is `MinVerMinimumMajorMinor`, the minor `master`
works towards, and `N` is MinVer's count of the commits since `master`'s nearest release tag, `v0.6.0`. No release tag
is put on a commit `master` contains (`scripts/release-source.sh` refuses one), so that tag stays the nearest and `N`
grows with every push: each push publishes a higher version than the one before, and every one sorts below its minor's
release candidates and release. There is no GitHub release, attestation or SBOM for a prerelease, and the templates
package is not published: the job deletes it from the downloaded packages before its checks and the push, since
NuGet.org's policy for the `prerelease` environment names the six library packages ([Repository
settings](#repository-settings)).

Prereleases exist so Tenantry Pro can build against `master` from a clean clone, and for early testers. They carry no
support or compatibility promise: the next one can change or remove any API, and security fixes are made only for the
versions the [security policy](.github/SECURITY.md#supported-versions) supports. NuGet picks a prerelease only when
asked for one (`dotnet add package Tenantry.Core --prerelease`, or the exact version). Like any version there, a
prerelease can be unlisted, but never deleted or replaced.

The job refuses any version but `X.Y.0-alpha.0.N` for the `MinVerMinimumMajorMinor` in `Directory.Build.props`, so a
version in a release branch's range fails rather than publishing. It also refuses an N at or below the highest N
NuGet.org has for that `X.Y`, unless the version is published already: a smaller N means a release tag is on `master`'s
history, or that a later push's run reached the job first, in which case the newer prerelease is out and nothing needs
doing. It checks the version on NuGet.org with `scripts/check-unpublished.sh`. When every package has it already, the
job checks that the published `Tenantry.Core` was built from the same commit: if so, the run is a rerun and skips the
push with a notice; if not, it fails. A version only some packages have fails the job, since a published package cannot
be replaced: unlist those packages, and the next push to `master` publishes the next version. Runs publish one at a
time, in the order they reach the job, and none is cancelled.

NuGet.org takes a few minutes to index a push, so a rerun in those minutes finds the version unpublished and its push
fails as a duplicate. Nothing needs doing; a rerun after that skips. A run that fails part way through the push can
leave every package published and some symbol packages missing; a rerun then skips the version, which stays without
those symbols. A rerun of the publish job alone fails at the download once the run's packages are more than 7 days old,
when GitHub deletes them; rerunning every job builds them again.

## The templates package

`templates/Tenantry.Templates.csproj` packs the `dotnet new` templates as `Tenantry.Templates`, at the same version as
the library packages. Pack writes that version into each template's `template.json`, so an application created from
the templates references the Tenantry packages of the same release (`--TenantryVersion` picks another). The Build &
Test job packs it into `./artifacts` with the libraries and writes its SBOM, and `scripts/smoke-templates.sh` installs
that package, creates each template, and builds and runs it against the libraries. A release pushes it after the
libraries, as the push takes the packages in name order; the prerelease job leaves it out. The consumer check runs
without it: its application cannot reference a template package. It has no symbol package, and pack does not
validate it (only the `src/` projects enable package validation), so `TenantryPackageBaseline` does not apply to it.

Before the first release that includes it, the maintainer does two things on NuGet.org:

1. Checks that the reserved ID prefix that gives the six library packages their verified mark covers
   `Tenantry.Templates`, so no one else can take the id first, unless the prefix is public, which lets anyone push
   under it. If it does not cover it, asks NuGet.org to reserve one that does
   ([ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)).
2. Checks the `release` environment's trusted publishing policy in NuGet.org's UI. Its scope, set for the whole
   policy, must allow pushing new packages and package versions, and its package list must include
   `Tenantry.Templates` or a pattern that covers it. If not, edit the policy, or replace it if its scope cannot be
   edited once created. Whether a pattern can name a package id NuGet.org does not have yet is not confirmed: check
   in the UI that the policy covers `Tenantry.Templates` before the first release that includes it.

If the second is missed, the release pushes the six library packages and their symbol packages, then fails on
`Tenantry.Templates`, before the GitHub release is created. Do not rerun it: as for any release that stops part way
([The release](#the-release)), fix the policy and release the next patch. The six library packages already published
are a complete set, so they need not be unlisted, and `TenantryPackageBaseline` moves to that version, as after any
release: all six exist at it, and pack never validates `Tenantry.Templates`.

## Repository settings

The `master` ruleset covers `release/*` too: changes only through pull requests with CI passing, linear history, and no
force pushes or deletion. Pushing a new `release/X.Y` with its commit, and `master`'s two commits from
`scripts/cut-release.sh`, rely on the maintainer's bypass of that ruleset. The `release` tag ruleset lets only the
maintainer create `v*` tags, and requires them signed, so tag with `git tag -s` as above; the release workflow does not
check the signature. SonarCloud analyses `master` and pull requests into it, not `release/*`: the organization's plan
serves only the main branch, so another branch's quality gate cannot be read, and what a release branch holds was
analysed on `master`, where every change lands first. The `release` environment must accept `v*` tags, and NuGet.org's
trusted publishing policy names this repository, `release.yml` and the `release` environment, with a scope that covers
the six library packages and `Tenantry.Templates`.

The `prerelease` environment has no required reviewers and accepts only the `master` branch. NuGet.org needs a second
trusted publishing policy for it, with the same package owner as the release's policy: repository owner `tenantry-org`,
repository `tenantry-core`, workflow file `ci.yml`, environment `prerelease`. Limit its scope to new versions of
existing packages, and name the six packages one by one rather than by a glob: `Tenantry.Core`, `Tenantry.EfCore`,
`Tenantry.AspNetCore`, `Tenantry.Options`, `Tenantry.Http` and `Tenantry.Caching`. Both workflows log in with the
NuGet.org user name in the `NUGET_USER` repository secret.

## After any release

- On the release branch, set `TenantryPackageBaseline` in `Directory.Build.props` to the version just released, once
  its library packages are on NuGet.org, and remove the `<TenantryPackageBaseline />` of a package released for the
  first time (`Tenantry.Templates` has none, as pack does not validate it). Pack then checks each package against that
  release, so a patch cannot break code compiled against it (Tenantry.Pro accepts any release in the minor). After an
  `X.Y.0`, do the same on `master`, and delete each `src/*/CompatibilitySuppressions.xml` on both: the release
  branch's patches break nothing, and the next minor's intended breaks are recorded afresh on `master`
  (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true`), and in the changelog.

## After a minor release

- Tenantry.Pro depends on Tenantry up to the next minor in 0.x (`[0.y.0, 0.(y+1).0)`), so each minor release of
  Tenantry is followed by a Tenantry.Pro release built against it, from its own release checklist.
- The site shows a new minor's docs once Tenantry.Pro has released it too. The home page's code sample follows the
  README's: update it on the site if the README's changed.
