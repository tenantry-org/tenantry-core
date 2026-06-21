# Tenantry

[![CI](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml)
[![Release](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=alert_status&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=coverage&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)
[![License](https://img.shields.io/github/license/tenantry-org/tenantry-core)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com)

A flexible, modern, and unopinionated multi-tenancy library for .NET.

Tenantry isolates each tenant's data in a **shared database** using row-level isolation: a
`TenantId` column on the entities you choose to make tenant-scoped. It does this without forcing
a base class on your entities, without a custom `DbContext`, and without taking over your request
pipeline. You pick the tenant key type, how tenants are resolved, and where they are stored — and
Tenantry wires the isolation in.

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");                          // where the tenant comes from
    tenant.UseInMemoryStore(tenants);                                 // where tenants are defined
    tenant.AddEfCoreIsolation(options => options.StrictIsolation = true); // how data is isolated
});
```

## Why Tenantry?

- **Unopinionated.** Your tenant key can be a `Guid`, `int`, `string`, or any type that is
  `IEquatable<T>` and `IParsable<T>`. Resolve tenants from a header, subdomain, route, claim, query
  string, or your own resolver. Store them in memory, a database, or anywhere behind an interface.
- **Interceptor-first isolation.** Tenant stamping and cross-tenant write protection work on **any**
  `DbContext` via an EF Core `SaveChanges` interceptor — no base class required. An optional
  `MultiTenantDbContext<TKey>` base class is provided for greenfield convenience.
- **Fails closed.** When no tenant is resolved, query filters match nothing rather than leaking every
  tenant's rows. Optional **strict mode** rejects cross-tenant writes *before* anything is persisted
  and warns when work runs without a tenant context.
- **HTTP and beyond.** `AddTenantry` covers ASP.NET Core (resolution middleware, access validation,
  endpoint metadata). `AddTenantryCore` brings the same isolation to console apps, worker services,
  and desktop UIs with no web stack.
- **Modern .NET.** Targets .NET 8, 9, and 10. The core and ASP.NET Core packages are trim- and
  Native-AOT-compatible (see [AOT & trimming](#aot--trimming)).

## Packages

| Package               | Version                                                                                                                | Description                                                                    |
|-----------------------|------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------|
| `Tenantry.Core`       | [![NuGet](https://img.shields.io/nuget/v/Tenantry.Core.svg)](https://www.nuget.org/packages/Tenantry.Core)             | Core interfaces, tenant scope, tenant store, and DI registration               |
| `Tenantry.EfCore`     | [![NuGet](https://img.shields.io/nuget/v/Tenantry.EfCore.svg)](https://www.nuget.org/packages/Tenantry.EfCore)         | EF Core integration — interceptor-based isolation, query filters, strict mode  |
| `Tenantry.AspNetCore` | [![NuGet](https://img.shields.io/nuget/v/Tenantry.AspNetCore.svg)](https://www.nuget.org/packages/Tenantry.AspNetCore) | ASP.NET Core integration — resolution middleware, resolvers, access validation |

`Tenantry.EfCore` and `Tenantry.AspNetCore` both depend on `Tenantry.Core`. Reference whichever
combination matches your host:

```bash
# ASP.NET Core app with EF Core isolation (most common)
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore

# Console / worker / desktop app with EF Core isolation
dotnet add package Tenantry.Core
dotnet add package Tenantry.EfCore
```

## Quick start (ASP.NET Core)

```csharp
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenantry<Guid>(tenant =>
{
    // 1. How is the tenant identified on each request? (resolvers are tried in order)
    tenant.ResolveFromHeader("X-Tenant-Id");

    // 2. Which tenants exist? (swap for a DB/cache-backed store in production)
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("…0001"), Name = "Acme" },
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("…0002"), Name = "Globex" },
    ]);
});

var app = builder.Build();

// Resolves the tenant and populates ITenantContext<Guid> for the rest of the request.
app.UseTenantry();

app.MapGet("/me", (ITenantContext<Guid> ctx) =>
        ctx.HasTenant ? Results.Ok(ctx.CurrentTenant!.Name) : Results.NotFound())
   .RequireTenant();

app.Run();
```

Add EF Core isolation by registering `tenant.AddEfCoreIsolation()` in the lambda above and calling
`options.AddTenantInterceptors(sp)` in your `AddDbContext` callback. See the
[EF Core integration guide](docs/efcore-integration.md) for the full picture.

## Quick start (console / worker — no ASP.NET Core)

There is no request to resolve a tenant from, so you open and close the tenant scope yourself:

```csharp
using Tenantry.Core;
using Tenantry.Core.Extensions;

builder.Services.AddTenantryCore<Guid>(tenant =>
{
    tenant.AddEfCoreIsolation(options => options.StrictIsolation = true);
});

// …later, around a unit of work:
var scope = sp.GetRequiredService<ITenantScope<Guid>>();
using (scope.BeginScope(new TenantDescriptor<Guid> { TenantId = tenantId, Name = "Acme" }))
{
    // EF Core reads are filtered to this tenant and writes are stamped with it.
    await db.SaveChangesAsync();
}
```

See the runnable [`Tenantry.Samples.EfCoreConsole`](samples/Tenantry.Samples.EfCoreConsole) project
and the [non-HTTP hosts guide](docs/non-http-hosts.md).

## AOT & trimming

Tenantry is built with the trim and AOT analyzers enabled and ships annotated for both. Support
differs by package because EF Core's query-filter mechanism requires runtime code generation:

| Package               | Trimming                | Native AOT                                                                 |
|-----------------------|-------------------------|----------------------------------------------------------------------------|
| `Tenantry.Core`       | ✅ Fully compatible     | ✅ Fully compatible (`IsAotCompatible`)                                     |
| `Tenantry.AspNetCore` | ✅ Fully compatible     | ✅ Fully compatible (`IsAotCompatible`) — see the `Aot` sample             |
| `Tenantry.EfCore`     | ✅ Trim-compatible      | ⚠️ Not AOT-compatible — query filters require dynamic code (see below)     |

- **`Tenantry.Core` and `Tenantry.AspNetCore`** are marked `IsAotCompatible` and `IsTrimmable` and
  carry no trim/AOT warnings. The [`Tenantry.Samples.Aot`](samples/Tenantry.Samples.Aot) project
  publishes with `PublishAot=true` against a slim host and source-generated JSON.
- **`Tenantry.EfCore`** is `IsTrimmable` but **not** AOT-compatible. The read-side query filters
  (`ApplyTenantFilters` and the `MultiTenantDbContext<TKey>` base class) build LINQ expression trees
  by reflecting over the EF Core model, so they are annotated `[RequiresDynamicCode]` and
  `[RequiresUnreferencedCode]`. This mirrors EF Core itself, which does not support Native AOT. The
  write-side interceptor does not generate code, but the integration as a whole should be treated as
  non-AOT.

Full details and guidance are in [AOT & trimming](docs/aot-and-trimming.md).

## Documentation

| Guide | What it covers |
|-------|----------------|
| [Getting started](docs/getting-started.md) | Install, your first tenant-aware app, end to end |
| [Core concepts](docs/core-concepts.md) | Tenant key, descriptor, context vs. scope, the `AsyncLocal` model |
| [Tenant stores](docs/tenant-stores.md) | In-memory and custom stores, service lifetimes |
| [ASP.NET Core integration](docs/aspnetcore-integration.md) | Registration, middleware, pipeline ordering, status codes |
| [Tenant resolution](docs/tenant-resolution.md) | Header, subdomain, route, claim, query-string, and custom resolvers |
| [Access control](docs/access-control.md) | Requiring tenants, access validators, claim-based validation |
| [EF Core integration](docs/efcore-integration.md) | Query filters, the interceptor, strict mode, migrations, admin queries |
| [Non-HTTP hosts](docs/non-http-hosts.md) | `AddTenantryCore` in console apps, workers, and background jobs |
| [AOT & trimming](docs/aot-and-trimming.md) | What is supported, per package, and why |
| [Troubleshooting](docs/troubleshooting.md) | Common pitfalls and how to diagnose them |

## Samples

| Sample | Demonstrates |
|--------|--------------|
| [`Quickstart`](samples/Tenantry.Samples.Quickstart) | Minimal ASP.NET Core setup, resolvers, access validators, endpoint metadata |
| [`EfCoreWeb`](samples/Tenantry.Samples.EfCoreWeb) | Realistic EF Core app: migrations, DB-backed store, mixed tenanted/global entities, admin queries |
| [`EfCoreConsole`](samples/Tenantry.Samples.EfCoreConsole) | EF Core isolation with no ASP.NET Core, using `AddTenantryCore` and manual scopes |
| [`Aot`](samples/Tenantry.Samples.Aot) | Native-AOT-published ASP.NET Core app |

## License

Licensed under the [Apache License 2.0](LICENSE).
