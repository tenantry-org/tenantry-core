Part of Tenantry, multi-tenancy for ASP.NET Core and EF Core: one call on your `DbContext`, no base class, and it fails
closed. A coding agent adding Tenantry to an application should follow
[the guide for AI coding agents](https://tenantry.dev/docs/core/ai-agents).

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")           // where the tenant comes from
    .ValidateTenantAccessByClaim("tenant_id")   // the caller's token must list it
    .UseInMemoryStore(tenants));                // where tenants are defined

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());                            // how data is isolated

app.UseAuthentication();
// Authorization before UseTenantry(), so an anonymous caller gets 401, not 403. With app.UseTenantResolution(), it
// comes after UseTenantry() instead.
app.UseAuthorization();
app.UseTenantry();
```

- Any key type (`Guid`, `int`, `string`), resolved from a header, subdomain, host, route, claim or your own resolver.
- With no tenant, queries return nothing and tenant-owned writes are refused. A write to another tenant's row is
  rejected, and `TenantId` is checked in every `UPDATE` and `DELETE`, so a forged key matches no row.
- A database per tenant, with per-tenant connection strings and `DbContext` pooling across tenant databases.

## Which packages

- `Tenantry.AspNetCore` and `Tenantry.EfCore` for a web app with EF Core.
- `Tenantry.Core` and `Tenantry.EfCore` for a console app or worker.
- `Tenantry.Http`, `Tenantry.Caching` and `Tenantry.Options` to send the tenant to other services, and keep
  `HybridCache` entries and options per tenant.

Tenantry is in beta until 1.0: a 0.x minor release can change the API, and the changelog says how to update.

[Get started](https://tenantry.dev/docs/core/getting-started) ·
[Docs](https://tenantry.dev/docs/core) ·
[Changelog](https://github.com/tenantry-org/tenantry-core/blob/master/CHANGELOG.md) ·
[Source](https://github.com/tenantry-org/tenantry-core) ·
[Tenantry Pro](https://tenantry.dev)

Licensed under the Apache License 2.0.
