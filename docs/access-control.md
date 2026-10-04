# Access control

Access control answers two separate questions: whether a request needs a tenant, and whether the caller may use the
tenant it named. The [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi) does both with JWT authentication, and its
tests check the 401, 403 and 400 responses.

## Requiring a tenant

By default, a request with no tenant continues without one, which suits health checks and sign-up. To reject it,
require a tenant for every endpoint or for one.

### Globally

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore(tenants);
    tenant.RequireTenantByDefault();   // every request must resolve a tenant…
});
```

With this on, a request that does not resolve a tenant gets `400 Bad Request`, and one whose tenant is unknown or
refused is rejected too ([status codes](aspnetcore-integration.md#status-codes)). Individual endpoints opt out with
`AllowMissingTenant()`. On an endpoint that does not require a tenant, a request whose tenant is unknown or refused
continues without one.

### Per-endpoint

Minimal APIs:

```csharp
app.MapGet("/orders", Handler).RequireTenant();          // must have a tenant
app.MapGet("/health", () => "ok").AllowMissingTenant();  // tenant optional even if required by default
```

Controllers (attributes target both classes and methods):

```csharp
using Microsoft.AspNetCore.Mvc;
using Tenantry.AspNetCore;

[RequireTenant]                       // applies to the whole controller
public class OrdersController : ControllerBase
{
    [AllowMissingTenant]              // …except this action
    [HttpGet("ping")]
    public IActionResult Ping() => Ok();
}
```

Endpoint metadata overrides `RequireTenantByDefault()`. If an endpoint has both `RequireTenant` and
`AllowMissingTenant`, the one added last wins. Use one per endpoint.

## Validating tenant access

A tenant that resolves and is in the store is not necessarily one the caller may use: a user of Acme should not be
able to send `X-Tenant-Id: globex`. Access validators run after the tenant is found in the store and before it is
made current. If validation fails, an endpoint that requires a tenant
responds `403 Forbidden`, and any other endpoint runs without a tenant; either way the refused tenant is never
current.

Once any validator is configured, a request for a tenant that does not exist gets the same response as one for a
tenant the caller may not use, so an authenticated user of one tenant cannot discover which others exist.

### Claim-based validation

When the caller's token lists the tenants it may use:

```csharp
tenant.ValidateTenantAccessByClaim("tenant_id");
```

This passes when any `tenant_id` claim on `HttpContext.User` matches the resolved tenant. It supports:

- repeated claims, each holding one id (`tenant_id: acme`, `tenant_id: globex`), and
- one claim holding a JSON array (`tenant_id: ["acme","globex"]`, or numbers `[1,2]` for numeric keys).

Each candidate value is parsed as `TKey`, with the invariant culture, and compared with the resolved tenant's id:
the claims list tenant ids, not other identifiers such as slugs. Requires `UseTenantry()` to run after
`UseAuthentication()`.

### Custom validators

A validator that needs your services, such as a `DbContext` that lists each user's memberships, is a class that
implements `ITenantAccessValidator<TKey>`. `ValidateTenantAccess<TValidator>()` creates it in each request's scope:

```csharp
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.AspNetCore;

public sealed class MembershipValidator(AppDbContext db) : ITenantAccessValidator<Guid>
{
    public async ValueTask<bool> ValidateAsync(HttpContext http, ITenantDescriptor<Guid> tenant, CancellationToken ct) =>
        await db.Set<Membership>().AnyAsync(
            m => m.UserId == http.User.FindFirstValue("sub") && m.TenantId == tenant.TenantId, ct);
}

public sealed class Membership
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public Guid TenantId { get; set; }
}
```

```csharp
tenant.ValidateTenantAccess<MembershipValidator>();
```

Like `UseResolver<T>()`, it returns the builder without its key type ([Registration](core-concepts.md#registration)).

A validator that needs only the request and the tenant can be a delegate, synchronous or asynchronous:

```csharp
// synchronous
tenant.ValidateTenantAccess((http, t) => !http.Request.Headers.ContainsKey("X-Block-Access"));

// asynchronous: it receives the request's cancellation token
tenant.ValidateTenantAccess(async (http, t, ct) =>
    await http.RequestServices.GetRequiredService<Entitlements>().CanAccessAsync(http.User, t.TenantId, ct));
```

### Combining validators: AND vs OR

Several validators combine with AND: each must pass, in the order they were added, and the first that refuses stops
the rest:

```csharp
tenant.ValidateTenantAccessByClaim("tenant_id");           // must hold the claim …
tenant.ValidateTenantAccess((http, _) => IsFromTrustedIp(http)); // … AND be from a trusted IP
```

For OR, put the alternatives in one validator:

```csharp
tenant.ValidateTenantAccess((http, t) =>
    http.User.HasClaim("tenant_id", t.TenantId.ToString())        // a matching tenant claim …
    || (http.Request.Headers.ContainsKey("X-Admin") && IsInternal(http))); // … OR an internal admin
```

That validator still combines with any others you add using AND. `HasClaim` compares the claim value as
a string; it does not read the JSON-array form that `ValidateTenantAccessByClaim` accepts.

### Suspended tenants

Refuse suspended tenants with `ValidateTenantActivity`, not an access validator: it also stops their background
work, jobs and messages. See [Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants).

## Putting it together

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");     // the tenant the caller asks for
    tenant.UseStore<EfCoreTenantStore>();
    tenant.RequireTenantByDefault();             // every endpoint needs a tenant
    tenant.ValidateTenantAccessByClaim("tenant_id"); // the token must list that tenant
    tenant.ValidateTenantActivity(t => !t.As<AppTenant>().IsSuspended); // and it must be active
});
```

A user whose token lists several tenants picks one with the header, and the validator checks it against all of
them. This is what the [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi) does.

See the [`Quickstart` sample](../samples/Tenantry.Samples.Quickstart) for a runnable demonstration of
required tenants, chained (AND) validators, and endpoint metadata.
