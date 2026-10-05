Install the templates, then create an application from one:

```bash
dotnet new install Tenantry.Templates
dotnet new tenantry-api -n Orders.Api
dotnet new tenantry-worker -n Orders.Worker
```

- `tenantry-api`: an ASP.NET Core API with EF Core. Callers authenticate with a JWT bearer token, pick a tenant with
  the `X-Tenant-Id` header, and Tenantry checks it against the token's `tenant` claims. Each tenant's rows are kept
  apart in one SQLite database. It redirects HTTP to HTTPS: behind a proxy that terminates TLS, configure
  `UseForwardedHeaders` for that proxy before `UseHsts` and `UseHttpsRedirection`, or every request is redirected
  again.
- `tenantry-worker`: a worker service with EF Core that runs each message as the tenant it names, with
  `RunInScopeAsync`, which refuses a tenant the store does not have.

Each references the Tenantry packages of the same version as the templates; `--TenantryVersion` picks another. The
projects target `net10.0` and reference Microsoft packages at 10.0.x, so an older SDK can install the templates and
create a project, but building it needs the .NET 10 SDK. A coding agent working on an application made from them
should follow [the guide for AI coding agents](https://tenantry.dev/docs/core/ai-agents).

[Docs](https://tenantry.dev/docs/core) ·
[Source](https://github.com/tenantry-org/tenantry-core) ·
[Changelog](https://github.com/tenantry-org/tenantry-core/blob/master/CHANGELOG.md)

Licensed under the Apache License 2.0.
