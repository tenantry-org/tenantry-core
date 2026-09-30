# Tenantry documentation

Tenantry is a flexible, modern, and unopinionated multi-tenancy library for .NET. It isolates each
tenant's data in a **shared database** using a `TenantId` column, wiring the isolation in through an
EF Core interceptor and global query filters, or gives each tenant **its own database** through
per-tenant connection strings ([database per tenant](efcore-integration.md#database-per-tenant)) —
without forcing a base class on your entities or taking over your request pipeline. Schema-per-tenant,
provisioning and migrations across tenant databases are in [Tenantry.Pro](https://tenantry.dev/docs/pro).

If you are new, start with **[Getting started](getting-started.md)** and **[Core concepts](core-concepts.md)**.

## Guides

1. **[Getting started](getting-started.md)** — install the packages and build a tenant-aware app end to end.
2. **[Core concepts](core-concepts.md)** — the tenant key, `ITenantDescriptor`, `ITenantContext` vs. `ITenantScope`, and the `AsyncLocal` model that ties them together.
3. **[Tenant stores](tenant-stores.md)** — the in-memory store, writing a custom `ITenantStore`, and service lifetimes.
4. **[ASP.NET Core integration](aspnetcore-integration.md)** — `AddTenantry`, the resolution middleware, pipeline ordering, and HTTP status codes.
5. **[Tenant resolution](tenant-resolution.md)** — header, subdomain, route, claim, and query-string resolvers, resolver ordering, and custom resolvers.
6. **[Access control](access-control.md)** — requiring tenants per-endpoint or globally, access validators, and claim-based validation.
7. **[EF Core integration](efcore-integration.md)** — query filters, the `SaveChanges` interceptor, the isolation policy, the optional base context, pooling, a database per tenant, migrations, and admin/cross-tenant queries.
8. **[Non-HTTP hosts](non-http-hosts.md)** — `AddTenantry` for console apps, worker services, and background jobs.
9. **[AOT & trimming](aot-and-trimming.md)** — exactly what is supported, per package, and why EF Core differs.
10. **[Compatibility](compatibility.md)** — supported .NET and EF Core versions, databases, and dependency ranges.
11. **[Troubleshooting](troubleshooting.md)** — common pitfalls and how to diagnose them.
12. **[API reference](api/README.md)** — every public type and member, generated from the XML documentation comments.

## How the pieces fit together

Tenantry has three responsibilities, each configured in the `AddTenantry` lambda:

| Responsibility | Question it answers | Configured with |
|----------------|---------------------|-----------------|
| **Resolution** | *Who is the tenant for this request/operation?* | `ResolveFromHeader(...)`, `ResolveFromClaim(...)`, … (ASP.NET Core), or `ITenantScopeFactory` (non-HTTP) |
| **Storage** | *Which tenants exist, and what are their details?* | `UseInMemoryStore(...)`, `UseStore<T>()` |
| **Isolation** | *How is each tenant's data kept separate?* | `AddEfCoreIsolation(...)`, plus `UseConnectionStrings(...)` for a database per tenant |

The flow on an ASP.NET Core request:

```
HTTP request
   │
   ▼
UseTenantry()  ──►  resolver(s) extract a raw id  ──►  parse to TKey  ──►  ITenantStore looks it up
   │                                                                              │
   │                                          (optional) access validators run    │
   ▼                                                                              ▼
ITenantContextSetter.Use(tenant)  sets the AsyncLocal tenant for the rest of the request
   │
   ▼
Your endpoint + EF Core
   ├─ reads  ──► global query filter restricts rows to ITenantContext.CurrentTenantId
   └─ writes ──► SaveChanges interceptor stamps/validates TenantId
```

In a console or worker app there is no request, so you make the tenant current yourself with
`ITenantScopeFactory` (or `ITenantContextSetter.Use`); everything below that line behaves identically.
