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

   It refuses unless `0.7` is `MinVerMinimumMajorMinor`, `release/0.7` and `v0.7.0` exist neither locally nor on
   `origin`, `## [Unreleased]` has entries, and no `## [0.7.0]` or `## Release 0.7.0` section exists yet. It creates
   `release/0.7` from `master`'s head, with one commit of its own, the one to tag: `## [Unreleased]` becomes
   `## [0.7.0] - YYYY-MM-DD` (today, UTC) below a new, empty `## [Unreleased]`, and each analyzer rule moves from
   `analyzers/*/AnalyzerReleases.Unshipped.md` to that folder's `AnalyzerReleases.Shipped.md`, under
   `## Release 0.7.0`. On `master`, it cherry-picks that commit, then raises `MinVerMinimumMajorMinor` to `0.8` in a
   second commit, so `master`'s prereleases are versioned above the branch's releases. It always raises the minor:
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
   (`git config user.signingkey`, the path of an SSH public key); without one, name yours in place of the example:

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
`release/X.Y` is also added to `master`'s `CHANGELOG.md`, placed by version.

## A patch

1. Fix it on `master` first, in a pull request as usual, unless the code is gone there.
2. In a pull request into `release/X.Y`, which runs CI, cherry-pick the fix and add the patch's
   section to `CHANGELOG.md` (`## [0.7.1] - YYYY-MM-DD`). Add the same section to `master`'s `CHANGELOG.md`, placed by
   version among the other releases. The release's notes come from the branch's copy.
3. Merge it and wait for CI to pass on the push to `release/X.Y`.
4. Before tagging, check that the tag is the line's next patch (`git tag --list 'v0.7.*'`), that `git log
   v0.7.0..origin/release/0.7` holds only what the patch should, that `TenantryPackageBaseline` names the line's last
   release, and that the `CHANGELOG.md` section is there. Then tag the branch's head and push the tag, as for a new
   minor.

Tag the head of a push to `release/X.Y`: CI runs once for each push, on its last commit, so a commit in the middle of
a push of several has no run of its own, and its tag is refused.

A minor older than the latest gets only security fixes ([security policy](.github/SECURITY.md#supported-versions)):
its patch's section has a `### Security` heading, and its security advisory is published once the packages are on
NuGet.org.

## The release

1. The workflow checks that the tag is a release tag on its branch's history, that CI passed on the push to
   `release/X.Y` that put the commit there, and that it has notes. A tag on a commit `master` contains, on another
   minor's branch, or on an unmerged branch fails before anything is built (`scripts/release-source.sh`).
2. It reruns CI on the tagged commit, then waits for approval in the `release` environment. Once approved, it pushes
   the six library packages and their symbol packages to NuGet.org, then `Tenantry.Templates`
   ([The templates package](#the-templates-package)), and creates the GitHub release, marked as the latest only if no
   higher version is released.
3. NuGet.org validates and indexes the packages, which takes a few minutes. They are available once
   `https://api.nuget.org/v3-flatcontainer/tenantry.core/index.json` lists the version. A version can be
   unlisted afterwards, but never deleted or replaced.

Before anything is built, the workflow checks that no package already has the version on NuGet.org
(`scripts/check-unpublished.sh`), and the push refuses a duplicate rather than skipping it. So a tag moved or pushed
again for a released version fails, instead of creating a GitHub release whose checksums do not match the published
packages. A dry run given a version checks it too.

If a release stops part way, after some packages were pushed, do not rerun it: the check refuses the version, since
it is partly published, and a published package cannot be replaced. Release the next patch instead, from the same
branch: add its `CHANGELOG.md` section, which says it replaces the incomplete release, and tag it. Unlist the
incomplete version's packages on NuGet.org, so no one picks a set that does not match. Leave `TenantryPackageBaseline`
at the last complete release, not the incomplete one: pack would look for each package at the baseline version, and
fail for the packages that were never published at it.

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
without it: its application cannot reference a template package. It has no symbol package, and no assembly whose API
pack could validate, so `TenantryPackageBaseline` does not apply to it.

Before the first release that includes it, the maintainer does two things on NuGet.org:

1. Checks that the reserved ID prefix that gives the six library packages their verified mark covers
   `Tenantry.Templates`, so no one else can take the id first; if it does not, asks NuGet.org to reserve one that
   does ([ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)).
2. Lets the `release` environment's trusted publishing policy push `Tenantry.Templates` as a new package: if its scope
   allows only new versions of existing packages, or names packages, add the glob pattern `Tenantry.Templates` with
   the scope to push new packages and new versions. A glob can name a package NuGet.org does not have yet.

If the second is skipped, the release pushes the six library packages and their symbol packages, then fails on
`Tenantry.Templates`, before the GitHub release is created. Do not rerun it: as for any release that stops part way
([The release](#the-release)), fix the policy and release the next patch. The six packages already published are a
complete set, so they need not be unlisted.

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
  every package of it is on NuGet.org, and remove the `<TenantryPackageBaseline />` of a package released for the
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
