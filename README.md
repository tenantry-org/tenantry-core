# Tenantry

[![CI](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml)
[![Release](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=alert_status&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=coverage&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)
[![License](https://img.shields.io/github/license/tenantry-org/tenantry-core)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS%20%7C%208.0%2C%209.0%20legacy-512BD4)](docs/compatibility.md)

Tenant isolation for ASP.NET Core and EF Core.

Tenantry keeps each tenant's data apart in EF Core, either in a shared database, where tenant-owned entities carry a
`TenantId` that every query and save is scoped to, or in a database per tenant. You choose the key type, how tenants
are resolved and where they are stored.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")   // where the tenant comes from
    .UseInMemoryStore(tenants));        // where tenants are defined

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());                    // how data is isolated
```

## Features

- Any key type that is `IEquatable<T>` and `IParsable<T>`: `Guid`, `int`, `string` and so on. Resolve tenants from a
  header, subdomain, host, route, claim or a resolver of your own, and keep them in any store behind an interface.
- `options.UseTenantry()` isolates any `DbContext`, pooled or not, with no base class and your own model
  configuration in any order.
- It fails closed: with no tenant, queries return nothing and tenant-owned writes are refused. A write to another
  tenant's row is rejected before saving, and `TenantId` is part of every `UPDATE` and `DELETE`, so a forged key
  matches no row. `Database.SqlQuery`, `ExecuteSql` and `IgnoreQueryFilters()` are not isolated
  ([details](docs/efcore-integration.md#what-is-and-isnt-isolated)).
- One `AddTenantry` serves ASP.NET Core, console apps, workers and desktop apps.
- Built for .NET 10. .NET 8 and 9 are supported until 10 November 2027 ([compatibility](docs/compatibility.md)).

[Tenantry.Pro](https://tenantry.dev), a subscription, adds schema per tenant, provisioning and migrations across
tenant databases, offboarding, audit logging, and the tenant in background jobs and messages.

## Packages

| Package               | Version                                                                                                                | Description                                                                    |
|-----------------------|------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------|
| `Tenantry.Core`       | [![NuGet](https://img.shields.io/nuget/v/Tenantry.Core.svg)](https://www.nuget.org/packages/Tenantry.Core)             | The current tenant, tenant stores, worker scopes and registration              |
| `Tenantry.EfCore`     | [![NuGet](https://img.shields.io/nuget/v/Tenantry.EfCore.svg)](https://www.nuget.org/packages/Tenantry.EfCore)         | EF Core isolation: query filters and checked saves                             |
| `Tenantry.AspNetCore` | [![NuGet](https://img.shields.io/nuget/v/Tenantry.AspNetCore.svg)](https://www.nuget.org/packages/Tenantry.AspNetCore) | Resolves each request's tenant: middleware, resolvers, access validation        |
| `Tenantry.Http`       | [![NuGet](https://img.shields.io/nuget/v/Tenantry.Http.svg)](https://www.nuget.org/packages/Tenantry.Http)             | Sends the current tenant to the services an `HttpClient` or gRPC client calls |
| `Tenantry.Caching`    | [![NuGet](https://img.shields.io/nuget/v/Tenantry.Caching.svg)](https://www.nuget.org/packages/Tenantry.Caching)       | Keeps `HybridCache` entries per tenant                                         |
| `Tenantry.Options`    | [![NuGet](https://img.shields.io/nuget/v/Tenantry.Options.svg)](https://www.nuget.org/packages/Tenantry.Options)       | Options values per tenant                                                      |

Each depends on `Tenantry.Core`. Reference the ones your host needs:

```bash
# ASP.NET Core app with EF Core isolation (most common)
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore

# Console / worker / desktop app with EF Core isolation
dotnet add package Tenantry.Core
dotnet add package Tenantry.EfCore

# Calling your other services as the current tenant
dotnet add package Tenantry.Http

# HybridCache entries per tenant
dotnet add package Tenantry.Caching

# Options with values per tenant
dotnet add package Tenantry.Options
```

Tenantry is in beta until 1.0: releases are numbered 0.x, and a minor release (0.4 to 0.5) can change the
API, with the steps to update in the [changelog](CHANGELOG.md).

## Quick start (ASP.NET Core)

The request names its tenant in a header, and the signed-in user's `tenant_id` claims must include it, so a caller
cannot select a tenant it does not belong to. The [`SecureApi` sample](samples/Tenantry.Samples.SecureApi) runs this
with JWT authentication.

```csharp
using Tenantry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication().AddJwtBearer();   // your sign-in, configured as usual

builder.Services.AddTenantry<Guid>(tenant =>
{
    // 1. How is the tenant identified on each request? (resolvers are tried in order)
    tenant.ResolveFromHeader("X-Tenant-Id");

    // 2. May the signed-in user use it? (their "tenant_id" claims must name it)
    tenant.ValidateTenantAccessByClaim("tenant_id");

    // 3. Which tenants exist? (swap for a DB/cache-backed store in production)
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Acme" },
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Globex" },
    ]);
});

var app = builder.Build();

// Resolves the tenant, checks it against the user, and populates ITenantContext<Guid> for the rest of the request.
app.UseAuthentication();
app.UseTenantry();

app.MapGet("/me", (ITenantContext<Guid> ctx) => Results.Ok(ctx.RequiredTenant.Name))
   .RequireTenant();          // 400 without a tenant, 403 for one the user may not use

app.Run();
```

Add EF Core isolation where you register your context, with `options.UseTenantry()`: queries are filtered to the
current tenant, and saves are stamped and checked ([EF Core integration](docs/efcore-integration.md)).

To start from a generated project instead, run `dotnet new install Tenantry.Templates`, then `dotnet new tenantry-api`
for an ASP.NET Core API or `dotnet new tenantry-worker` for a worker service. The projects target `net10.0`, so
building one needs the .NET 10 SDK.

## Quick start (console or worker)

With no request to resolve a tenant from, open a tenant scope around each unit of work:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant.UseStore<EfCoreTenantStore>());
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString).UseTenantry());

// …later, in a hosted service (inject ITenantScopeFactory<Guid> scopes and ITenantLookup<Guid> tenants).
// With an id (e.g. from a queue message), look the tenant up in the store and run as it. A tenant the store does not
// hold, or one that ValidateTenantActivity refuses, throws.
await scopes.RunInScopeAsync(message.TenantId, (scope, ct) => HandleAsync(scope, message, ct), cancellationToken);

// With a tenant already loaded from the store, open a fresh DI scope with it current.
foreach (var tenant in await tenants.GetAllTenantsAsync(cancellationToken))
{
    await using var scope = scopes.CreateScope(tenant);
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // EF Core reads are filtered to this tenant and writes are stamped with it.
    await db.SaveChangesAsync(cancellationToken);
}
```

`CreateScope` trusts the descriptor it is given: it does not look it up or check whether it is active, so pass it only
a tenant you already hold.

See the [`EfCoreConsole` sample](samples/Tenantry.Samples.EfCoreConsole) and [Non-HTTP hosts](docs/non-http-hosts.md).

## AOT & trimming

`Tenantry.Core`, `Tenantry.AspNetCore`, `Tenantry.Http`, `Tenantry.Caching` and `Tenantry.Options` support trimming
and Native AOT. `Tenantry.EfCore` supports trimming only, as EF Core does, and its EF Core entry points are annotated
([details](docs/aot-and-trimming.md)).

## Documentation

| Guide | What it covers |
|-------|----------------|
| [Getting started](docs/getting-started.md) | Install, your first tenant-aware app, end to end |
| [Core concepts](docs/core-concepts.md) | Tenant key, descriptor, context vs. scope, the `AsyncLocal` model |
| [Tenant stores](docs/tenant-stores.md) | In-memory and custom stores, service lifetimes, caching tenants |
| [ASP.NET Core integration](docs/aspnetcore-integration.md) | Registration, middleware, pipeline ordering, status codes, events |
| [Tenant resolution](docs/tenant-resolution.md) | Header, subdomain, host, route, claim, query-string, and custom resolvers; slugs and custom domains |
| [Authentication per tenant](docs/authentication-per-tenant.md) | JWT bearer, OpenID Connect and cookie settings per tenant |
| [Access control](docs/access-control.md) | Requiring tenants, access validators, claim-based validation |
| [EF Core integration](docs/efcore-integration.md) | Query filters, the interceptor, isolation policy, migrations, admin queries |
| [Owned and multi-table entities](docs/efcore-advanced.md) | How owned entities and split tables are checked, all-or-nothing saves, unsupported models |
| [Non-HTTP hosts](docs/non-http-hosts.md) | `AddTenantry` in console apps, workers, and background jobs |
| [Testing](docs/testing.md) | Tests with Tenantry's real services: scopes, `WebApplicationFactory`, EF Core isolation |
| [Diagnostics](docs/diagnostics.md) | Log event ids, the `tenant.id` trace tag and log scope, the resolution metric |
| [AOT & trimming](docs/aot-and-trimming.md) | What is supported, per package, and why |
| [Compatibility](docs/compatibility.md) | Supported .NET and EF Core versions, databases, and dependency ranges |
| [Troubleshooting](docs/troubleshooting.md) | Common pitfalls and how to diagnose them |
| [Migrating from Finbuckle.MultiTenant](docs/migrating-from-finbuckle.md) | Finbuckle's concepts and EF Core setup in Tenantry, and what changes |
| [Analyzers](docs/analyzers.md) | The build warnings for code that leaves tenant data unprotected, and how to configure them |
| [For AI coding agents](docs/ai-agents.md) | Steps for an agent adding Tenantry, an isolation test, common mistakes, rules for AGENTS.md |

## Samples

| Sample | Demonstrates |
|--------|--------------|
| [`SecureApi`](samples/Tenantry.Samples.SecureApi) | **Start here for production:** JWT authentication, tenant selection validated against the caller's claims, required tenants, EF Core isolation, integration tests |
| [`Quickstart`](samples/Tenantry.Samples.Quickstart) | Minimal ASP.NET Core setup, resolvers, access validators, endpoint metadata |
| [`EfCoreWeb`](samples/Tenantry.Samples.EfCoreWeb) | An EF Core app with migrations, DB-backed store, mixed tenanted/global entities, admin queries |
| [`EfCoreConsole`](samples/Tenantry.Samples.EfCoreConsole) | EF Core isolation with no ASP.NET Core, using `AddTenantry` and manual scopes |
| [`DatabasePerTenant`](samples/Tenantry.Samples.DatabasePerTenant) | A database per tenant with `UseConnectionStrings` and a pooled `AddDbContextPerTenantDatabase`, plus worker scopes |
| [`Aot`](samples/Tenantry.Samples.Aot) | Native-AOT-published ASP.NET Core app: header, subdomain and custom resolvers, problem details |

## License

Licensed under the [Apache License 2.0](LICENSE).
