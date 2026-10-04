# Security Policy

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull
requests.**

Instead, report them privately via GitHub's **[Private Vulnerability Reporting](https://github.com/tenantry-org/tenantry-core/security/advisories/new)**
(Security → Advisories → "Report a vulnerability"). This keeps the details confidential until a
fix is available.

Please include:

- A description of the vulnerability and its impact
- Steps to reproduce (a minimal repro or proof of concept if possible)
- Affected package(s) and version(s)
- Any suggested mitigation, if known

## What to expect

- **Acknowledgement** within a few business days.
- An assessment and, where applicable, a coordinated fix and release.
- Credit in the release notes / advisory if you'd like it (let us know).

## Supported versions

Security fixes are released for the latest released minor version of each package, as its next patch or in the
next release. Until 1.0, they are also released for the Tenantry Core minor versions that the latest two minor
versions of Tenantry.Pro run on, so that every Tenantry.Pro release that still gets security patches runs on a
Tenantry Core that gets them too. Of the minor versions released before Tenantry.Pro goes on sale, only the latest
gets security fixes, and only while it is the latest: older ones are not patched, and their users upgrade. A patch to
a minor version older than the latest is released from that minor's `release/X.Y` branch. The supported versions
from 1.0 will be set out here before 1.0 is released. The prereleases each push to `master` publishes
(`0.7.0-alpha.0.126`) are not supported: a fix reaches them only in a later prerelease.

| Package | Supported until 1.0 |
|---|---|
| `Tenantry.Core` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
| `Tenantry.AspNetCore` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
| `Tenantry.EfCore` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
| `Tenantry.Http` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
| `Tenantry.Caching` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
| `Tenantry.Options` | the latest minor, and the minors the latest two Tenantry.Pro minors run on, as above |
