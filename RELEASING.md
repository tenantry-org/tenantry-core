# Releasing Tenantry

Every release is a `vX.Y.Z` tag, release candidates (`vX.Y.Z-rc.1`) included, on the head of its own branch,
`release/X.Y`; never on `master` or another minor's branch. Changes land on `master`, and a release branch takes only
what its releases need, cherry-picked from `master`. The release workflow (`.github/workflows/release.yml`) does the
rest. Only the maintainer can push `v*` tags.

## A new minor

1. On `master`, in a pull request as usual, rename `## [Unreleased]` in `CHANGELOG.md` to `## [x.y.0] - YYYY-MM-DD`
   and start a new, empty `## [Unreleased]` above it. That section is the GitHub release's notes, and a tag without one
   fails before anything is published. It starts with the steps to update from the previous minor. In the same pull
   request, move each analyzer rule the release ships from `analyzers/*/AnalyzerReleases.Unshipped.md` to that
   folder's `AnalyzerReleases.Shipped.md`, under `## Release x.y.0`. Merge it and wait for CI to pass on `master`.
2. Cut the minor's branch from `master`'s head, before the `vX.Y.0` tag or its first release candidate, and push it:

   ```sh
   git branch release/0.7 origin/master
   git push origin release/0.7
   ```

   Pushing the new branch runs CI, SonarCloud included, on its head. Wait for it to pass: the release requires that
   run, on a push to `release/X.Y`. Then, in a pull request into `master`, raise `MinVerMinimumMajorMinor` in
   `Directory.Build.props` to the next minor (`0.8`), so `master`'s own packages are versioned above the branch's
   releases.
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

## The templates package

`templates/Tenantry.Templates.csproj` packs the `dotnet new` templates, and CI checks them
(`scripts/smoke-templates.sh`), but no release publishes the package yet. To ship it with a release: pack it into
`./artifacts` in `build-test.yml`'s Pack step; give it an SBOM in the step after; leave it out of the consumer check
(`check-package-consumer.sh`, shared with Tenantry Pro), whose application cannot reference a template package; and
reserve the `Tenantry.Templates` id on NuGet.org. The release workflow then publishes it with the rest.

## Repository settings

`release/*` branches need a ruleset like `master`'s: changes only through pull requests with CI passing, and no force
pushes or deletion. SonarCloud's default long-lived branch pattern, `(branch|release)-.*`, does not match
`release/0.7`: set it to `release/.*` (the project's Administration → Branches and Pull Requests) before the first
release branch is pushed, since a branch's kind is fixed at its first analysis, and keep a quality gate on it. The
`release` environment must accept `v*` tags, and NuGet.org's trusted publishing policy names this repository,
`release.yml` and the `release` environment.

## After any release

- On the release branch, set `TenantryPackageBaseline` in `Directory.Build.props` to the version just released, once
  every package of it is on NuGet.org, and remove the `<TenantryPackageBaseline />` of a package released for the
  first time. Pack then checks each package against that release, so a patch cannot break code compiled against it
  (Tenantry.Pro accepts any release in the minor). After an `X.Y.0`, do the same on `master`, and delete each
  `src/*/CompatibilitySuppressions.xml` on both: the release branch's patches break nothing, and the next minor's
  intended breaks are recorded afresh on `master` (`dotnet pack -p:ApiCompatGenerateSuppressionFile=true`), and in
  the changelog.

## After a minor release

- Tenantry.Pro depends on Tenantry up to the next minor in 0.x (`[0.y.0, 0.(y+1).0)`), so each minor release of
  Tenantry is followed by a Tenantry.Pro release built against it, from its own release checklist.
- The site shows a new minor's docs once Tenantry.Pro has released it too. The home page's code sample follows the
  README's: update it on the site if the README's changed.
