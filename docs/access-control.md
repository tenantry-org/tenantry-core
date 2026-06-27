# Access control

Resolution answers *who is the tenant*. Access control answers two further questions:

1. **Is a tenant required** for this request? (Should a request with no resolved tenant be rejected?)
2. **Is this caller allowed** to act as the resolved tenant? (Can user X access tenant Y?)

These are independent and can be used together.

## Requiring a tenant

By default a request with no resolved tenant simply proceeds with no tenant context — useful for
health checks, sign-up, and other anonymous endpoints. To reject such requests you can require a
tenant globally or per-endpoint.

### Globally

```csharp
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

For **OR** semantics, use `ValidateTenantAccessAny`. It takes one or more *groups*; the request passes
if **any group** passes, and a group passes only if **all** validators within it pass (AND inside a
group, OR across groups):

```csharp
tenant.ValidateTenantAccessAny(
    // group A: a valid tenant claim …
    group => group.ValidateTenantAccessByClaim("tenant_id"),
    // … OR group B: an admin header AND an internal network flag
    group => group
        .ValidateTenantAccess((http, _) => http.Request.Headers.ContainsKey("X-Admin"))
        .ValidateTenantAccess((http, _) => IsInternal(http)));
```

`ValidateTenantAccessAny` is itself just another validator in the chain, so it still combines with any
plainly-added validators using AND. In other words: all top-level `ValidateTenantAccess(...)` calls
**and** the `ValidateTenantAccessAny(...)` result must all pass. At least one group must be supplied,
or the call throws `ArgumentException`.

## Putting it together

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromClaim("tenant_id");        // bind tenant to the token
    tenant.ResolveFromHeader("X-Tenant-Id");     // fallback for service calls
    tenant.UseStore<EfCoreTenantStore>();
    tenant.RequireTenantByDefault();             // no anonymous tenant access
    tenant.ValidateTenantAccessByClaim("tenant_id"); // caller must be entitled to the tenant
    tenant.AddEfCoreIsolation(o => o.DetectSpoofedWrites = true);
});
```

See the [`Quickstart` sample](../samples/Tenantry.Samples.Quickstart) for a runnable demonstration of
required tenants, AND/OR validators, and endpoint metadata.
