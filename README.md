# Tenantry

[![CI](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/ci.yml)
[![Release](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml/badge.svg)](https://github.com/tenantry-org/tenantry-core/actions/workflows/release.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=alert_status&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=tenantry-org_tenantry-core&metric=coverage&token=3a836e3680d4d63886210902f77daf99c80b85be)](https://sonarcloud.io/summary/new_code?id=tenantry-org_tenantry-core)

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
