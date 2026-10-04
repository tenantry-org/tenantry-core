# ASP.NET Core integration

`Tenantry.AspNetCore` turns an incoming HTTP request into a resolved tenant. It adds, to the builder of
`AddTenantry<TKey>(...)`:

- tenant resolvers (header, subdomain, host, route, claim, query string, your own): see
  [Tenant resolution](tenant-resolution.md);
- access validation and endpoint metadata: see [Access control](access-control.md);
- `ConfigureResolution(...)`: whether endpoints need a tenant, the [status codes](#status-codes) of rejections, and
  the [events](#events) raised when a request's tenant is made current or a request is rejected;

and `app.UseTenantry()`, the resolution middleware, which also checks the registration when the application starts.
Its logs, traces and metrics are described in [Diagnostics](diagnostics.md).

## Registration

```csharp
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")          // resolution (at least one required)
    .UseInMemoryStore(tenants)                 // storage (exactly one required)
    .RequireTenantByDefault()                  // policy (optional)
    .ValidateTenantAccessByClaim("tenant_id")); // access control (optional)
```

`Tenantry.AspNetCore` adds these methods to the `AddTenantry` builder. The first one you call registers the
middleware's services. See [Registration](core-concepts.md#registration) for the rules every builder method follows.

### Startup validation

`app.UseTenantry()` throws `InvalidOperationException` when the pipeline is built if no resolver or no store is
registered. A web application that registers resolvers but never calls `app.UseTenantry()` fails to start, because
no request would have a tenant. A host that serves no requests (a worker) is not checked. Registering a second store,
or a second key type, throws at once.

## The middleware

```csharp
var app = builder.Build();
app.UseTenantry();
```

For each request, the middleware:

1. Tries each registered resolver in registration order and takes the first identifier one returns (`null`,
   an empty string or whitespace counts as none, and the next resolver runs).
2. If no resolver produced an identifier:
   - if a tenant is required for this request (see [Access control](access-control.md)), rejects it
     (`400 Bad Request`) and stops;
   - otherwise continues the pipeline with no tenant.
3. Finds the tenant the identifier names, with `ITenantLookup<TKey>.FindByIdentifierAsync`, which calls your
   store's `FindByIdentifierAsync` (by default: parse the identifier as `TKey` and look the id up) and serves it from
   the cache with [`CacheTenants`](tenant-stores.md#caching). If none, a request that requires a tenant is
   rejected (see [Status codes](#status-codes)).
4. Checks the tenant is [active](tenant-stores.md#suspended-and-inactive-tenants), then runs the
   [access validators](access-control.md) in the order they were added. If either refuses, a request that requires a
   tenant is rejected (`403 Forbidden`).
5. Makes the tenant current (`ITenantContextSetter.Use`) for the remainder of the request, tags the request's
   trace span `tenant.id` and opens a log scope with `TenantId`, and raises [`OnResolved`](#events). The tenant is
   restored when the request ends.

A request to an endpoint that does not require a tenant is never rejected: when its identifier names no tenant, or
one that is refused, it continues without a tenant, as if it had no identifier. So a `www.` host or a stale header
does not break your login and health endpoints.

Resolvers and access validators added by type (`UseResolver<TResolver>()`, `ValidateTenantAccess<TValidator>()`) are
created in the request's service scope, so they can depend on a scoped `DbContext`. The store is read through
`ITenantLookup<TKey>` ([lifetimes](tenant-stores.md#registration-and-lifetimes)).

### Status codes

| Situation (on an endpoint that requires a tenant) | Default status | Option |
|---------------------------------------------------|----------------|--------|
| No resolver produced an identifier | `400 Bad Request` | `MissingTenantStatusCode` |
| The identifier names no tenant (with the default lookup: it does not parse, is the key type's default, or is not in the store) | `404 Not Found` | `TenantNotFoundStatusCode` |
| The tenant is not active (`ValidateTenantActivity`), or an access validator refused it | `403 Forbidden` | `AccessDeniedStatusCode` |
| The identifier names no tenant, and access validators are configured | same as access denied | `AccessDeniedStatusCode` |

With access validators, a tenant that does not exist gets exactly the response of one the caller may not use,
so an authenticated user of one tenant cannot probe for others. Change the codes with `ConfigureResolution`:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<EfCoreTenantStore>()
    .ConfigureResolution(options =>
    {
        options.RequireTenantByDefault = true;
        options.MissingTenantStatusCode = StatusCodes.Status401Unauthorized;
    }));
```

A rejection's body is empty unless an `IProblemDetailsService` is registered (`builder.Services.AddProblemDetails()`),
in which case it is `application/problem+json` with the status, a title (`Tenant required`, `Tenant not found`,
`Tenant access denied`) and a short detail. The body never repeats the identifier the request sent. Customise the
problem details as usual, with `AddProblemDetails(options => options.CustomizeProblemDetails = …)`, or write a
response of your own in [`OnRejected`](#events).

## Events

`ConfigureResolution` sets two handlers:

- `OnResolved` runs when a request's tenant has been made current, before the rest of the pipeline, with the
  request and the tenant: to add the tenant to your own telemetry, say. Changes it makes to ambient state, such as
  `CultureInfo.CurrentCulture` or an `AsyncLocal`, reach the rest of the pipeline only if the handler is not an
  `async` method (set them and `return Task.CompletedTask;`): an `async` handler's changes are undone when it
  returns, even those made before its first `await`. For a per-tenant culture, use
  `UseRequestLocalization` with a culture provider that reads `ITenantContext<TKey>`. To refuse a tenant, use an
  [access validator](access-control.md#validating-tenant-access).
- `OnRejected` runs when an endpoint that requires a tenant rejects a request, before Tenantry writes its response.
  It is told the `Reason` (`Missing`, `NotFound` or `AccessDenied`), the `StatusCode` Tenantry would send, the
  `Identifier` the request sent and, for `AccessDenied`, the refused `Tenant`. Change `StatusCode`, or write your
  own response and call `HandleResponse()`, so Tenantry writes none:

```csharp
using Tenantry.AspNetCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .RequireTenantByDefault()
    .ConfigureResolution(o => o.OnRejected = context =>
    {
        // A browser that asks for a workspace that does not exist goes to the marketing site.
        if (context.Reason == TenantRejectionReason.NotFound)
        {
            context.HttpContext.Response.Redirect("https://example.com/");
            context.HandleResponse();
        }

        return Task.CompletedTask;
    }));
```

`Reason` is the real reason, for your logs; `StatusCode` hides it as described under [Status codes](#status-codes).
Keep it hidden in a response of your own, and do not repeat `Identifier`, which is request input, without encoding
it.

## Pipeline ordering

Put `UseTenantry()` before anything that needs the tenant: your endpoints, authorization that depends on it, and
EF Core work driven by the request. Put it after `UseAuthentication()` when you resolve or validate from claims, and
after routing, so it sees endpoint metadata (`WebApplication` adds routing first; a custom pipeline must call
`UseRouting()` before `UseTenantry()`). For authentication settings that differ per tenant, also call
`UseTenantResolution()` before `UseAuthentication()`: see [Authentication per tenant](authentication-per-tenant.md).

If the middleware runs before routing, a request without a tenant to an endpoint that requires one is still
rejected, but `RequireTenantByDefault()` then applies to `AllowMissingTenant()` endpoints too, and route values are
not there to resolve from. If the authentication middleware runs after it, `ResolveFromClaim` sees no user. The
middleware logs each mistake once ([event ids](diagnostics.md#logs) 1007 and 1008). A user signed in by other code
after the middleware, such as authorization with a scheme that is not the default, is not seen by `ResolveFromClaim`
either, and is not warned about: make that scheme the default.

A typical order:

```csharp
app.UseAuthentication();
app.UseTenantry();        // resolves the tenant (reads User if using claims; reads endpoint metadata)
app.UseAuthorization();
app.MapControllers();     // or minimal API endpoints
```

## Reading the tenant in your code

Inject `ITenantContext<TKey>` anywhere. On an endpoint that requires a tenant (`RequireTenant()`, or
`RequireTenantByDefault()`), a request without one is refused before the handler runs, so the handler need not
check:

```csharp
app.MapGet("/me", (ITenantContext<Guid> ctx) => Results.Ok(ctx.CurrentTenant!.Name))
    .RequireTenant();
```

Check `HasTenant` only where the tenant is optional: on an endpoint that does not require one.

Read `CurrentTenantId` for work outside EF Core, which applies the tenant for you.

## MVC / controllers

Everything above applies to controllers too. The endpoint metadata is `[RequireTenant]` and `[AllowMissingTenant]` on
controllers, and `.RequireTenant()` and `.AllowMissingTenant()` on minimal APIs. See [Access control](access-control.md).
