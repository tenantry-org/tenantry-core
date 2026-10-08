# Tenantry documentation

Tenantry keeps each tenant's data apart in EF Core applications, in a shared database or a database per tenant. In a
shared database, tenant-owned entities carry a `TenantId` that every query and save is scoped to. With a
[database per tenant](efcore-integration.md#database-per-tenant), each tenant's contexts connect to its own database.
Your entities need no base class, and Tenantry does not take over your request pipeline. Schema per tenant,
provisioning and migrations across tenant databases are in [Tenantry Pro](https://tenantry.dev/pro), a paid
subscription.

Start with [Getting started](getting-started.md) and [Core concepts](core-concepts.md).

## Guides

1. [Getting started](getting-started.md): install the packages and build a tenant-aware app end to end.
2. [Core concepts](core-concepts.md): the tenant key, `ITenantDescriptor`, `ITenantContext` vs. `ITenantScope`, and the `AsyncLocal` model that ties them together.
3. [Tenant stores](tenant-stores.md): writing an `ITenantStore` over your own tenants table, its lifetime, suspended tenants, caching and invalidation, and the in-memory store for tests.
4. [ASP.NET Core integration](aspnetcore-integration.md): `AddTenantry`, the resolution middleware, pipeline ordering, HTTP status codes, and resolution events.
5. [Tenant resolution](tenant-resolution.md): header, subdomain, host, route, claim, and query-string resolvers, resolver ordering, custom resolvers, and identifiers other than the tenant id.
6. [Access control](access-control.md): requiring tenants per-endpoint or globally, access validators, and claim-based validation.
7. [Calling other services](http-propagation.md): sending the current tenant with `HttpClient` and gRPC calls (`Tenantry.Http`), and resolving it in the called service.
8. [Caching per tenant](caching.md): `HybridCache` entries and output-cached responses kept per tenant (`Tenantry.Caching`, `IsolateOutputCache()`), shared entries, and invalidation.
9. [Options per tenant](per-tenant-options.md): `IOptionsSnapshot<T>` and `IOptionsMonitor<T>` values per tenant (`Tenantry.Options`), built from your configuration and the tenant, and cleared when the tenant changes.
10. [Authentication per tenant](authentication-per-tenant.md): JWT bearer, OpenID Connect and cookie settings per tenant, with `UseTenantResolution()` before authentication, and a scheme per tenant.
11. [ASP.NET Core Identity](aspnetcore-identity.md): users kept per tenant in a shared database, user names unique within a tenant, and sign-in cookies tied to their tenant.
12. [EF Core integration](efcore-integration.md): query filters, the `SaveChanges` interceptor, isolation options, pooling, a database per tenant, migrations, and cross-tenant queries.
13. [Owned and multi-table entities](efcore-advanced.md): how owned entities, entities split across tables and many-to-many join rows are checked, saves that succeed or fail as a whole, and models that cannot be isolated.
14. [Non-HTTP hosts](non-http-hosts.md): `AddTenantry` for worker services, scheduled jobs, CLI tools and desktop apps, and running work as a tenant.
15. [Testing](testing.md): tests that use Tenantry's real services, with tenant scopes, `WebApplicationFactory` and EF Core isolation.
16. [Diagnostics](diagnostics.md): log event ids, the `tenant.id` trace tag and log scope, and the resolution metric.
17. [AOT & trimming](aot-and-trimming.md): what is supported, per package, and why EF Core differs.
18. [Compatibility](compatibility.md): supported .NET and EF Core versions, databases, and dependency ranges.
19. [Troubleshooting](troubleshooting.md): common pitfalls and how to diagnose them.
20. [Migrating from Finbuckle.MultiTenant](migrating-from-finbuckle.md): how Finbuckle's concepts and EF Core setup map to Tenantry, and what changes.
21. [Analyzers](analyzers.md): the warnings `Tenantry.EfCore` and `Tenantry.AspNetCore` give for code that leaves tenant data unprotected, and how to configure them.
22. [For AI coding agents](ai-agents.md): the steps an agent follows to add Tenantry, a test that proves isolation, the common mistakes, and rules to copy into AGENTS.md.
23. [API reference](api/README.md): every public type and member, generated from the XML documentation comments.

## How the pieces fit together

Tenantry has three parts, each configured in the `AddTenantry` lambda:

| Part | What it decides | Configured with |
|------|-----------------|-----------------|
| Resolution | The tenant of a request or operation | `ResolveFromHeader(...)`, `ResolveFromClaim(...)`, … (ASP.NET Core), or `ITenantScopeFactory` (non-HTTP) |
| Storage | Which tenants exist, and their details | `UseInMemoryStore(...)`, `UseStore<T>()` |
| Isolation | How each tenant's data is kept apart | `options.UseTenantry()` on each `DbContext`, or `UseConnectionStrings(...)` and `AddDbContextPerTenantDatabase<TContext>(...)` for a database per tenant |

The flow on an ASP.NET Core request:

```
HTTP request
   │
   ▼
UseTenantry()  ──►  resolver(s) extract an identifier  ──►  ITenantStore finds the tenant it names (cached, optionally)
   │                                                                              │
   │                                          (optional) access validators run    │
   ▼                                                                              ▼
ITenantContextSetter.MakeCurrent(tenant)  sets the AsyncLocal tenant for the rest of the request
   │
   ▼
Your endpoint + EF Core
   ├─ reads  ──► global query filter restricts rows to ITenantContext.CurrentTenantId
   └─ writes ──► SaveChanges interceptor stamps/validates TenantId
```

A console or worker app has no request, so you make the tenant current yourself with `ITenantScopeFactory` (or
`ITenantContextSetter.MakeCurrent`). Everything below that line behaves the same.
