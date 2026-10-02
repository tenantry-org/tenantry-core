# Releasing Tenantry

A release is a `v*` tag on a commit on `master`; the release workflow (`.github/workflows/release.yml`) does the
rest. Only the maintainer can push `v*` tags.

## Before the tag

1. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` and start a new, empty
   `## [Unreleased]` above it. That section is the GitHub release's notes, and a tag without one fails before
   anything is published. A minor release's section starts with the steps to update from the previous minor.
2. Push to `master` and wait for CI, SonarCloud included, to pass.
3. Rehearse (optional): Actions → Release → Run workflow on `master`, with the tag as the version. It runs the
   release's checks, builds the same packages, and shows what a release would publish and its notes.

## The release

1. Tag the commit and push the tag:

   ```sh
   git tag -a vx.y.z -m "Tenantry x.y.z (beta)"
   git push origin vx.y.z
   ```

2. The workflow checks that the tag is on `master` and has notes, reruns CI on the tagged commit, then waits for
   approval in the `release` environment. Once approved, it pushes the packages and their symbol packages to
   NuGet.org and creates the GitHub release.
3. NuGet.org validates and indexes the packages, which takes a few minutes. They are available once
   `https://api.nuget.org/v3-flatcontainer/tenantry.core/index.json` lists the version. A version can be
   unlisted afterwards, but never deleted or replaced.

## After a minor release

- Tenantry.Pro depends on Tenantry up to the next minor in 0.x (`[0.y.0, 0.(y+1).0)`), so each minor release of
  Tenantry is followed by a Tenantry.Pro release built against it, from its own release checklist.
- The site shows a new minor's docs once Tenantry.Pro has released it too. The home page's code sample follows the
  README's: update it on the site if the README's changed.
