# Access control

Resolution answers *who is the tenant*. Access control answers two further questions:

1. **Is a tenant required** for this request? (Should a request with no resolved tenant be rejected?)
2. **Is this caller allowed** to act as the resolved tenant? (Can user X access tenant Y?)

These are independent and can be used together. The [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi)
combines both with JWT authentication, and its integration tests check the 401, 403 and 400 responses.

## Requiring a tenant

By default a request with no resolved tenant simply proceeds with no tenant context — useful for
health checks, sign-up, and other anonymous endpoints. To reject such requests you can require a
tenant globally or per-endpoint.

### Globally

```csharp
using Tenantry.AspNetCore.Extensions;

builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore(tenants);
    tenant.RequireTenantByDefault();   // every request must resolve a tenant…
});
```

With this on, any request that does not resolve a tenant gets `400 Bad Request`. Individual endpoints
opt out with `AllowMissingTenant()`.

### Per-endpoint

Minimal APIs:

```csharp
app.MapGet("/orders", Handler).RequireTenant();          // must have a tenant
app.MapGet("/health", () => "ok").AllowMissingTenant();  // tenant optional even if required by default
```

Controllers (attributes target both classes and methods):

```csharp
using Microsoft.AspNetCore.Mvc;
using Tenantry.AspNetCore.Attributes;

[RequireTenant]                       // applies to the whole controller
public class OrdersController : ControllerBase
{
    [AllowMissingTenant]              // …except this action
    [HttpGet("ping")]
    public IActionResult Ping() => Ok();
}
```

**Precedence.** Endpoint metadata overrides the global default. If both `RequireTenant` and
`AllowMissingTenant` are present on the same endpoint, the metadata added **last** wins (the middleware
scans metadata from last to first and takes the first match). Keep it to one per endpoint to avoid
confusion. When no metadata is present, `RequireTenantByDefault()` decides.

## Validating tenant access

Resolving and finding a tenant does not mean the *caller* is allowed to use it. A user authenticated as
Acme should not be able to send `X-Tenant-Id: globex`. Access validators run **after** the tenant is
found in the store but **before** the scope is opened; if validation fails the request gets
`403 Forbidden` and no scope is set.

### Claim-based validation

The common case — the caller's token carries the tenant(s) they may access:

```csharp
tenant.ValidateTenantAccessByClaim("tenant_id");
```

This passes when any `tenant_id` claim on `HttpContext.User` matches the resolved tenant. It supports:

- **repeated claims**, each holding a single id (`tenant_id: acme`, `tenant_id: globex`), and
- a single claim holding a **JSON array** (`tenant_id: ["acme","globex"]`, or numbers
  `[1,2]` for numeric keys).

Each candidate value is parsed with `TKey.TryParse` and compared with the resolved tenant id.
Requires `UseTenantry()` to run after `UseAuthentication()`.

### Custom validators

Add synchronous or asynchronous validators with full access to the `HttpContext` and the resolved
tenant:

```csharp
// synchronous
tenant.ValidateTenantAccess((http, tenantDescriptor) =>
    !http.Request.Headers.ContainsKey("X-Block-Access"));

// asynchronous
tenant.ValidateTenantAccess(async (http, tenantDescriptor, ct) =>
    await _entitlements.CanAccessAsync(http.User, tenantDescriptor.TenantId, ct));
```

### Combining validators: AND vs OR

Multiple validators added directly are combined with logical **AND** — every one must pass:

```csharp
tenant.ValidateTenantAccessByClaim("tenant_id");           // must hold the claim …
tenant.ValidateTenantAccess((http, _) => IsFromTrustedIp(http)); // … AND be from a trusted IP
```

For **OR** semantics, put the alternatives in one validator:

```csharp
tenant.ValidateTenantAccess((http, t) =>
    http.User.HasClaim("tenant_id", t.TenantId.ToString())        // a matching tenant claim …
    || (http.Request.Headers.ContainsKey("X-Admin") && IsInternal(http))); // … OR an internal admin
```

That validator still combines with any others you add using AND. `HasClaim` compares the claim value as
a string; it does not read the JSON-array form that `ValidateTenantAccessByClaim` accepts.

### Suspended tenants

Your store returns suspended tenants too (see
[Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)), so refuse them with a
validator that reads the status from your own descriptor type:

```csharp
tenant.ValidateTenantAccess((http, t) => t is Tenant { IsActive: true });
```

Access validators run only in the HTTP middleware. `ITenantScopeFactory`, `ITenantScope.BeginScope` and
background jobs never call them, so background work must check the tenant's status itself.

## Putting it together

```csharp
using Tenantry.AspNetCore.Extensions;
using Tenantry.EfCore.Extensions;

builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromClaim("tenant_id");        // bind tenant to the token
    tenant.ResolveFromHeader("X-Tenant-Id");     // fallback for service calls
    tenant.UseStore<EfCoreTenantStore>();
    tenant.RequireTenantByDefault();             // no anonymous tenant access
    tenant.ValidateTenantAccessByClaim("tenant_id"); // caller must be entitled to the tenant
    tenant.ValidateTenantAccess((_, t) => t is Tenant { IsActive: true }); // and it must be active
    tenant.AddEfCoreIsolation(o => o.DetectSpoofedWrites = true);
});
```

See the [`Quickstart` sample](../samples/Tenantry.Samples.Quickstart) for a runnable demonstration of
required tenants, chained (AND) validators, and endpoint metadata.
