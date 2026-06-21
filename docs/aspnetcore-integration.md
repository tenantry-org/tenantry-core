# ASP.NET Core integration

`Tenantry.AspNetCore` turns an incoming HTTP request into a resolved tenant scope. It adds:

- `AddTenantry<TKey>(...)` — registration and the fluent builder.
- `UseTenantry()` — the resolution middleware.
- Tenant **resolvers** (header, subdomain, route, claim, query string) — see [Tenant resolution](tenant-resolution.md).
- **Access validation** and endpoint metadata — see [Access control](access-control.md).
- Startup validation that fails fast on misconfiguration.

## Registration

```csharp
using Tenantry.AspNetCore.Extensions;

builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");          // resolution (at least one required)
    tenant.UseInMemoryStore(tenants);                 // storage (exactly one required)
    tenant.AddEfCoreIsolation();                      // isolation (optional; needs Tenantry.EfCore)
    tenant.RequireTenantByDefault();                  // policy (optional)
    tenant.ValidateTenantAccessByClaim("tenant_id");  // access control (optional)
});
```

`AddTenantry` calls `AddTenantryCore` internally, so the `ITenantContext<TKey>`/`ITenantScope<TKey>`
services and any `AddEfCoreIsolation` registration are all set up by this one call. The builder
(`IAspNetCoreTenantBuilder<TKey>`) extends the core `ITenantBuilder<TKey>`, so store and isolation
methods are available alongside the ASP.NET-specific ones.

### Startup validation

`AddTenantry` registers an `IHostedService` that runs at startup and throws `InvalidOperationException`
if:

- **no resolver** is registered (you forgot `ResolveFrom…`/`UseResolver`), or
- **no store** is registered (you forgot `UseInMemoryStore`/`UseStore`).

This converts a class of silent runtime bugs into an immediate, descriptive startup failure.

## The middleware

```csharp
var app = builder.Build();
app.UseTenantry();
```

For each request, the middleware:

1. Tries each registered resolver **in registration order** and takes the **first non-null** raw id.
2. If no resolver produced an id:
   - if a tenant is **required** for this request (see [Access control](access-control.md)), responds
     `400 Bad Request` and stops;
   - otherwise continues the pipeline with **no** tenant context.
3. Parses the raw id with `TKey.TryParse`. On failure, responds `400 Bad Request`.
4. Looks the id up via `ITenantStore<TKey>.GetTenantAsync`. If `null`, responds `404 Not Found`.
5. Runs any [access validators](access-control.md). If access is denied, responds `403 Forbidden`.
6. Opens the tenant scope (`BeginScope`) for the remainder of the request and adds `TenantId`/
   `TenantName` to the logging scope for log correlation. The scope is disposed when the request ends.

The store is resolved from the **request's** service scope, so a scoped, `DbContext`-backed store
works correctly.

### Status codes

| Situation | Status | Configurable |
|-----------|--------|--------------|
| Tenant required but none resolved | `400 Bad Request` | internal default |
| Raw id fails `TKey.TryParse` | `400 Bad Request` | no |
| Tenant id not found in store | `404 Not Found` | no |
| Access validator denied | `403 Forbidden` | internal default |

The "required but missing" and "access denied" codes have internal defaults (`400`/`403`); they are
not currently exposed as public options.

## Pipeline ordering

`UseTenantry()` must run **before** anything that needs the resolved tenant — your endpoints,
authorization that depends on the tenant, and EF Core work driven by the request.

Two ordering rules matter:

- **After authentication when resolving from claims.** `ResolveFromClaim` and `ValidateTenantAccessByClaim`
  read `HttpContext.User`, which is only populated after `app.UseAuthentication()`. Place
  `UseTenantry()` after it.
- **After routing for endpoint metadata.** `RequireTenant()`/`AllowMissingTenant()` are endpoint
  metadata, so the middleware must run after routing has selected an endpoint to honour them.
  `WebApplication` adds routing automatically and places it early, so for minimal APIs and controllers
  this generally just works. If you build a custom pipeline, ensure `UseRouting()` precedes
  `UseTenantry()`.

A typical order:

```csharp
app.UseAuthentication();
app.UseTenantry();        // resolves the tenant (reads User if using claims; reads endpoint metadata)
app.UseAuthorization();
app.MapControllers();     // or minimal API endpoints
```

## Reading the tenant in your code

Inject `ITenantContext<TKey>` anywhere:

```csharp
app.MapGet("/me", (ITenantContext<Guid> ctx) =>
    ctx.HasTenant ? Results.Ok(ctx.CurrentTenant!.Name) : Results.NotFound());
```

You rarely need to read `CurrentTenantId` for data access — the EF Core query filter and interceptor
apply it for you. Read it when you need the tenant for non-EF logic (per-tenant file paths, external
API keys, logging, etc.).

## MVC / controllers

Everything above applies to controllers too. The endpoint metadata helpers exist as attributes for
controllers — `[RequireTenant]` and `[AllowMissingTenant]` — and as builder methods
(`.RequireTenant()`, `.AllowMissingTenant()`) for minimal APIs. See [Access control](access-control.md).
