# Tenantry

[![CI](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml)
[![Release](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml)

A flexible, modern, and unopinionated multi-tenancy library for .NET.

## Packages

| Package | Description |
|---|---|
| `Tenantry.Core` | Core interfaces, tenant scope, tenant store, and DI registration |
| `Tenantry.EfCore` | EF Core integration — interceptor-based isolation, query filters, strict mode |
| `Tenantry.AspNetCore` | ASP.NET Core integration — resolution middleware, resolvers, access validation |

## Quick Start

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.UseInMemoryStore(new[]
    {
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Acme" }
    });

    tenant.ResolveFromHeader("X-Tenant-Id");
});
```

## Documentation

See the [docs](docs/) directory.
