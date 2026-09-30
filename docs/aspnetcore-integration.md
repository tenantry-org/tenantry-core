# ASP.NET Core integration

`Tenantry.AspNetCore` turns an incoming HTTP request into a resolved tenant. It adds, to the builder of
`AddTenantry<TKey>(...)`:

- Tenant **resolvers** (header, subdomain, route, claim, query string, custom) — see [Tenant resolution](tenant-resolution.md).
- **Access validation** and endpoint metadata — see [Access control](access-control.md).
- `ConfigureResolution(...)` — whether endpoints need a tenant, and the [status codes](#status-codes) of rejections.

and `app.UseTenantry()`, the resolution middleware, which also checks the registration when the application starts.

## Registration

```csharp
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")          // resolution (at least one required)
    .UseInMemoryStore(tenants)                 // storage (exactly one required)
    .RequireTenantByDefault()                  // policy (optional)
    .ValidateTenantAccessByClaim("tenant_id")); // access control (optional)
```

There is one `AddTenantry`, in `Tenantry.Core`, for every kind of host. It registers the core services
(`ITenantContext<TKey>`, `ITenantContextSetter<TKey>`, `ITenantScopeFactory<TKey>`, `ITenantStoreAccessor<TKey>`),
and its builder, `ITenantBuilder<TKey>`, gains the ASP.NET Core methods above when `Tenantry.AspNetCore` is
referenced; the first one you call registers the middleware's services. Every builder method returns the
builder, so calls chain in any order (with one exception: `UseResolver<TResolver>()` returns the builder without
its key type, so call it last). `AddTenantry` can be called again, from another part of the application, to add
to the same registration.

### Startup validation

`app.UseTenantry()` checks the registration when the pipeline is built, before the application serves a
request, and throws `InvalidOperationException` if:

- **Tenantry's request resolution is not registered** (you did not call `AddTenantry` with a `ResolveFrom…` or
  `UseResolver` method),
- **no resolver** is registered, or
- **no store** is registered (you forgot `UseInMemoryStore`/`UseStore`).

A second store registration, and `AddTenantry` with another tenant key type, throw where they are made. This
converts a class of silent runtime bugs into an immediate, descriptive startup failure.

## The middleware

```csharp
var app = builder.Build();
app.UseTenantry();
```

For each request, the middleware:

1. Tries each registered resolver **in registration order** and takes the **first non-null** raw id.
2. If no resolver produced an id:
   - if a tenant is **required** for this request (see [Access control](access-control.md)), rejects it
     (`400 Bad Request`) and stops;
   - otherwise continues the pipeline with **no** tenant context.
3. Parses the raw id with `TKey.TryParse`. An id that does not parse, or parses to the key type's default
   (`Guid.Empty`, `0`), is not a tenant: a request that requires a tenant is rejected (`400 Bad Request`).
4. Looks the id up via `ITenantStore<TKey>.GetTenantAsync`. If `null`, a request that requires a tenant is
   rejected (`404 Not Found`, or the access-denied response when access validators are configured).
5. Runs any [access validators](access-control.md). If access is denied, a request that requires a tenant is
   rejected (`403 Forbidden`).
6. Makes the tenant current (`ITenantContextSetter.Use`) for the remainder of the request and adds `TenantId`/
   `TenantName` to the logging scope for log correlation. The tenant is restored when the request ends.

A request to an endpoint that does **not** require a tenant is never rejected: when its identifier is not
valid, names no tenant, or names one an access validator refuses, it continues without a tenant, as if it had no
identifier. So a `www.` host or a stale header does not break your login and health endpoints, and a caller
learns nothing about which tenants exist.

The store is resolved from the **request's** service scope, so a scoped, `DbContext`-backed store
works correctly.

### Status codes

| Situation (on an endpoint that requires a tenant) | Default status | Option |
|---------------------------------------------------|----------------|--------|
| No resolver produced an id | `400 Bad Request` | `MissingTenantStatusCode` |
| The id does not parse, or is the key type's default | `400 Bad Request` | `InvalidTenantStatusCode` |
| The id is not in the store | `404 Not Found` | `TenantNotFoundStatusCode` |
| An access validator refused the tenant (including a suspended tenant your validator refuses) | `403 Forbidden` | `AccessDeniedStatusCode` |
| The id is not in the store, **and access validators are configured** | same as access denied | `AccessDeniedStatusCode` |

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
in which case it is `application/problem+json` with the status, a title (`Tenant required`, `Invalid tenant`,
`Tenant not found`, `Tenant access denied`) and a short detail. The body never repeats the identifier the request
sent. Customise the problem details as usual, with `AddProblemDetails(options => options.CustomizeProblemDetails = …)`.

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
