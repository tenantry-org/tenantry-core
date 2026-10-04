# Releasing Tenantry

A release is a `v*` tag on a commit on `master`, or for a patch to an older minor on its `release/X.Y` branch
([Patching an older minor](#patching-an-older-minor)); the release workflow (`.github/workflows/release.yml`) does the
rest. Only the maintainer can push `v*` tags.

## Before the tag

1. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` and start a new, empty
   `## [Unreleased]` above it. That section is the GitHub release's notes, and a tag without one fails before
   anything is published. A minor release's section starts with the steps to update from the previous minor.
2. Push to `master` and wait for CI, SonarCloud included, to pass. The release fails if it has not.
3. Rehearse (optional): Actions → Release → Run workflow on `master`, with the tag as the version. It runs the
   release's checks, builds the same packages, and shows what a release would publish and its notes.

## The release

1. Tag the commit and push the tag:

   ```sh
   git tag -a vx.y.z -m "Tenantry x.y.z (beta)"
   git push origin vx.y.z
   ```

2. The workflow checks that the tag is on `master` (or its `release/X.Y` branch), that CI passed on the push that put
   the commit there, and that it has notes, reruns CI on the tagged commit, then waits for approval in the `release`
   environment. Once approved, it pushes the packages and their symbol packages to NuGet.org and creates the GitHub
   release, marked as the latest only if no higher version is released.
3. NuGet.org validates and indexes the packages, which takes a few minutes. They are available once
   `https://api.nuget.org/v3-flatcontainer/tenantry.core/index.json` lists the version. A version can be
   unlisted afterwards, but never deleted or replaced.

## Patching an older minor

Until the next minor is released, a patch is released from `master` as above. Once it is, a security fix for an older
minor in the supported window ([security policy](.github/SECURITY.md#supported-versions)) is released from a
`release/X.Y` branch, and only security fixes go there. The release workflow accepts a `vX.Y.Z` tag on `master` or on
`release/X.Y`, and a tag below the newest release only on `release/X.Y` (`scripts/release-source.sh`).

1. Cut the branch once, when the first such fix is needed, from the minor's last release tag, and push it:

   ```sh
   git branch release/0.6 v0.6.1
   git push origin release/0.6
   ```

   A tag made before release branches were supported has workflows that run only on `master`. On a branch cut from
   one, cherry-pick the commit "CI and the release workflow accept patches from release branches" first, in a pull
   request into the branch, so CI runs on the branch and its tags can be released.
2. In a pull request into the branch, set `TenantryPackageBaseline` in `Directory.Build.props` to the minor's last
   release (`0.6.1`), and delete each `src/*/CompatibilitySuppressions.xml`: they record the minor's breaks against
   the one before it, and a patch has none against its own minor.
3. Fix it on `master` first, in a pull request as usual, unless the code is gone there. Then cherry-pick the fix onto
   the branch in a pull request into `release/X.Y`, which runs CI, SonarCloud included.
4. Add the patch's section to `CHANGELOG.md`, `## [0.6.2] - YYYY-MM-DD` with a `### Security` heading, in the same
   pull request into the branch, and then the same section to `master`'s `CHANGELOG.md`, placed by version among the
   other releases. The release's notes come from the branch's copy.
5. Merge the pull request into the branch and wait for CI, SonarCloud included, to pass on the push. The release
   workflow requires that run, on the push to `release/X.Y` that put the commit there. Rehearse it if you like:
   Actions → Release → Run workflow on `release/X.Y`, with the tag as the version.
6. Before tagging, check that the tag is the line's next patch (`git tag --list 'v0.6.*'`), that `git log
   v0.6.1..origin/release/0.6` holds only the fix and the steps above, that `TenantryPackageBaseline` names the line's
   last release, and that the CHANGELOG section is there. Then tag the branch's head and push the tag:

   ```sh
   git tag -a v0.6.2 -m "Tenantry 0.6.2 (beta)" origin/release/0.6
   git push origin v0.6.2
   ```

   The release then runs as above, approval included. The GitHub release is not marked as the latest, as a newer
   version is released. Publish the security advisory once the packages are on NuGet.org.
7. On the branch, set `TenantryPackageBaseline` to the patch just released (After any release, below). `master` keeps
   its own.

## After any release

- Set `TenantryPackageBaseline` in `Directory.Build.props` to the version just released, and remove the
  `<TenantryPackageBaseline />` of a package released for the first time. Pack then checks each package against that
  release, so a patch cannot break code compiled against it (Tenantry.Pro accepts any release in the minor). A
  minor release may break the API on purpose: record each break in the project's `CompatibilitySuppressions.xml`
  (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true`) and in the changelog.

## After a minor release

- Tenantry.Pro depends on Tenantry up to the next minor in 0.x (`[0.y.0, 0.(y+1).0)`), so each minor release of
  Tenantry is followed by a Tenantry.Pro release built against it, from its own release checklist.
- The site shows a new minor's docs once Tenantry.Pro has released it too. The home page's code sample follows the
  README's: update it on the site if the README's changed.
