# Releasing Tenantry

Every release is a `vX.Y.Z` tag, release candidates (`vX.Y.Z-rc.1`) included, on the head of its own branch,
`release/X.Y`; never on `master` or another minor's branch. Changes land on `master`, and a release branch takes only
what its releases need, cherry-picked from `master`. The release workflow (`.github/workflows/release.yml`) does the
rest. Only the maintainer can push `v*` tags.

## A new minor

1. On `master`, in a pull request as usual, rename `## [Unreleased]` in `CHANGELOG.md` to `## [x.y.0] - YYYY-MM-DD`
   and start a new, empty `## [Unreleased]` above it. That section is the GitHub release's notes, and a tag without one
   fails before anything is published. It starts with the steps to update from the previous minor. Merge it and wait
   for CI to pass on `master`.
2. Cut the minor's branch from `master`'s head, before the `vX.Y.0` tag or its first release candidate, and push it:

   ```sh
   git branch release/0.7 origin/master
   git push origin release/0.7
   ```

   Pushing the new branch runs CI, SonarCloud included, on its head. Wait for it to pass: the release requires that
   run, on a push to `release/X.Y`.
3. Rehearse (optional): Actions → Release → Run workflow on `release/X.Y`, with the tag as the version. It runs the
   release's checks, builds the same packages, and shows what a release would publish and its notes.
4. Tag the branch's head and push the tag:

   ```sh
   git tag -a v0.7.0 -m "Tenantry 0.7.0 (beta)" origin/release/0.7
   git push origin v0.7.0
   ```

A release candidate is tagged the same way (`v0.7.0-rc.1`) on the same branch, with a `CHANGELOG.md` section of its
own (`## [0.7.0-rc.1] - YYYY-MM-DD`), and `v0.7.0` follows from the branch.

## A patch

1. Fix it on `master` first, in a pull request as usual, unless the code is gone there.
2. In a pull request into `release/X.Y`, which runs CI, SonarCloud included, cherry-pick the fix and add the patch's
   section to `CHANGELOG.md` (`## [0.7.1] - YYYY-MM-DD`). Add the same section to `master`'s `CHANGELOG.md`, placed by
   version among the other releases. The release's notes come from the branch's copy.
3. Merge it and wait for CI, SonarCloud included, to pass on the push to `release/X.Y`.
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
   `release/X.Y` that put the commit there, and that it has notes. A tag on `master` alone, on another minor's branch,
   or on an unmerged branch fails before anything is built (`scripts/release-source.sh`).
2. It reruns CI on the tagged commit, then waits for approval in the `release` environment. Once approved, it pushes
   the packages and their symbol packages to NuGet.org and creates the GitHub release, marked as the latest only if
   no higher version is released.
3. NuGet.org validates and indexes the packages, which takes a few minutes. They are available once
   `https://api.nuget.org/v3-flatcontainer/tenantry.core/index.json` lists the version. A version can be
   unlisted afterwards, but never deleted or replaced.

## A line released before release branches (0.6)

A `release/0.6` branch cut from a v0.6.x tag has workflows that run only on `master`, so its CI does not run and its
tags are refused. In a pull request into the branch, take `master`'s `.github/workflows/release.yml` and
`scripts/release-source.sh` as they are, and in `ci.yml` keep the branch's lines except two: the `release/*` branches
in the `push` and `pull_request` triggers, and the concurrency group by commit for a push (`ci-${{ github.ref }}-${{
github.event_name == 'pull_request' && 'pr' || github.sha }}`), without which a third push can cancel a queued run and
leave a commit with no CI. Cherry-picking master's changes to `ci.yml` conflicts on v0.6.x, as its concurrency group
and `run_sonar` changed after 0.6.0. In the same pull request, set `TenantryPackageBaseline` to the line's last release
and delete each `src/*/CompatibilitySuppressions.xml`, as after any release below.

## Repository settings

`release/*` branches need a ruleset like `master`'s: changes only through pull requests with CI passing, and no force
pushes or deletion. SonarCloud analyses them as long-lived branches by its default pattern; check the project's
settings keep it so, with a quality gate. The `release` environment must accept `v*` tags, and NuGet.org's trusted
publishing policy names this repository, `release.yml` and the `release` environment.

## After any release

- On the release branch, set `TenantryPackageBaseline` in `Directory.Build.props` to the version just released, and
  remove the `<TenantryPackageBaseline />` of a package released for the first time. Pack then checks each package
  against that release, so a patch cannot break code compiled against it (Tenantry.Pro accepts any release in the
  minor). After an `X.Y.0`, do the same on `master`, and delete each `src/*/CompatibilitySuppressions.xml` on both: the
  release branch's patches break nothing, and the next minor's intended breaks are recorded afresh on `master`
  (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true`), and in the changelog.

## After a minor release

- Tenantry.Pro depends on Tenantry up to the next minor in 0.x (`[0.y.0, 0.(y+1).0)`), so each minor release of
  Tenantry is followed by a Tenantry.Pro release built against it, from its own release checklist.
- The site shows a new minor's docs once Tenantry.Pro has released it too. The home page's code sample follows the
  README's: update it on the site if the README's changed.
