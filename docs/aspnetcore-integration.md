# ASP.NET Core integration

`Tenantry.AspNetCore` turns an incoming HTTP request into a resolved tenant. It adds, to the builder of
`AddTenantry<TKey>(...)`:

- Tenant **resolvers** (header, subdomain, host, route, claim, query string, custom) — see [Tenant resolution](tenant-resolution.md).
- **Access validation** and endpoint metadata — see [Access control](access-control.md).
- `ConfigureResolution(...)` — whether endpoints need a tenant, the [status codes](#status-codes) of rejections, and
  the [events](#events) raised when a request's tenant is made current or a request is rejected.

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

`app.UseTenantry()` checks the registration when the pipeline is built, before the application serves a
request, and throws `InvalidOperationException` if:

- **Tenantry's request resolution is not registered** (you did not call `AddTenantry` with a `ResolveFrom…` or
  `UseResolver` method),
- **no resolver** is registered, or
- **no store** is registered (you forgot `UseInMemoryStore`/`UseStore`).

The other way round, a web application that registers request resolution but never calls `app.UseTenantry()`
fails to start: without the middleware, no request would have a tenant, and endpoints marked `RequireTenant()`
would run without one. A host that serves no requests (a worker) is not checked.

A second store registration, and `AddTenantry` with another tenant key type, throw where they are made. This
converts a class of silent runtime bugs into an immediate, descriptive startup failure.

## The middleware

```csharp
var app = builder.Build();
app.UseTenantry();
```

For each request, the middleware:

1. Tries each registered resolver **in registration order** and takes the **first** identifier one returns (a
   resolver that returns `null` or an empty string has none).
2. If no resolver produced an identifier:
   - if a tenant is **required** for this request (see [Access control](access-control.md)), rejects it
     (`400 Bad Request`) and stops;
   - otherwise continues the pipeline with **no** tenant context.
3. Finds the tenant the identifier names, with `ITenantLookup<TKey>.FindByIdentifierAsync`, which calls your
   store's `FindByIdentifierAsync` (by default: parse the identifier as `TKey` and look the id up) and serves it from
   the cache with [`CacheTenants`](tenant-stores.md#caching). If none, a request that requires a tenant is
   rejected (`404 Not Found`, or the access-denied response when access validators are configured).
4. Runs the [access validators](access-control.md), in the order they were added. If one refuses, a request that
   requires a tenant is rejected (`403 Forbidden`).
5. Makes the tenant current (`ITenantContextSetter.Use`) for the remainder of the request, tags the request's
   trace span `tenant.id` and opens a log scope with `TenantId`, and raises [`OnResolved`](#events). The tenant is
   restored when the request ends.

A request to an endpoint that does **not** require a tenant is never rejected: when its identifier names no
tenant, or names one an access validator refuses, it continues without a tenant, as if it had no identifier. So a
`www.` host or a stale header does not break your login and health endpoints, and a caller learns nothing about
which tenants exist.

Resolvers and access validators added by type (`UseResolver<TResolver>()`, `ValidateTenantAccess<TValidator>()`) are
created in the **request's** service scope, so they can depend on a scoped `DbContext`. The store is resolved from a scope of `ITenantLookup<TKey>`'s own for each lookup.

### Status codes

| Situation (on an endpoint that requires a tenant) | Default status | Option |
|---------------------------------------------------|----------------|--------|
| No resolver produced an identifier | `400 Bad Request` | `MissingTenantStatusCode` |
| The identifier names no tenant (with the default lookup: it does not parse, is the key type's default, or is not in the store) | `404 Not Found` | `TenantNotFoundStatusCode` |
| The tenant is not active (`ValidateTenantActivity`), or an access validator refused it | `403 Forbidden` | `AccessDeniedStatusCode` |
| The identifier names no tenant, **and access validators are configured** | same as access denied | `AccessDeniedStatusCode` |

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

`Reason` is the real reason, for your logs: with access validators, an unknown tenant's `StatusCode` is the
access-denied one, so a caller cannot tell it from a refused tenant. Keep that in a response of your own, and do
not repeat `Identifier`, which is request input, without encoding it.

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

You rarely need to read `CurrentTenantId` for data access — the EF Core query filter and interceptor
apply it for you. Read it when you need the tenant for non-EF logic (per-tenant file paths, external
API keys, logging, etc.).

## MVC / controllers

Everything above applies to controllers too. The endpoint metadata helpers exist as attributes for
controllers — `[RequireTenant]` and `[AllowMissingTenant]` — and as builder methods
(`.RequireTenant()`, `.AllowMissingTenant()`) for minimal APIs. See [Access control](access-control.md).
