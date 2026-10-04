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

Security fixes are released for the latest released minor version of each package. Until 1.0, they are also released
for the Tenantry Core minor versions that the latest two minor versions of Tenantry.Pro run on, so that every
Tenantry.Pro release that still gets security patches runs on a Tenantry Core that gets them too. The supported
versions from 1.0 will be set out here before 1.0 is released.

| Package | Supported until 1.0 |
|---|---|
| `Tenantry.Core` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
| `Tenantry.AspNetCore` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
| `Tenantry.EfCore` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
| `Tenantry.Http` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
| `Tenantry.Caching` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
| `Tenantry.Options` | the latest minor, and the minors the latest two Tenantry.Pro minors run on |
