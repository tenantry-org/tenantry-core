# ASP.NET Core integration

`Tenantry.AspNetCore` turns an incoming HTTP request into a resolved tenant. It adds, to the builder of
`AddTenantry<TKey>(...)`:

- tenant resolvers (header, subdomain, host, route, claim, query string, your own): see
  [Tenant resolution](tenant-resolution.md);
- access validation and endpoint metadata: see [Access control](access-control.md);
- `ConfigureResolution(...)`: whether endpoints need a tenant, the [status codes](#status-codes) of rejections, and
  the [events](#events) raised when a request's tenant is made current or a request is rejected;

and `app.UseTenantry()`, the resolution middleware, which also checks the registration when the application starts.
For hubs and circuits, see [SignalR and Blazor Server](#signalr-and-blazor-server).
[Diagnostics](diagnostics.md) describes its logs, traces and metrics.

## Registration

```csharp
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")          // resolution (at least one required)
    .UseInMemoryStore(tenants)                 // storage (exactly one required)
    .RequireTenantByDefault()                  // policy (optional)
    .ValidateTenantAccessByClaim("tenant_id")); // access control (optional)
```

The first `Tenantry.AspNetCore` method you call registers the middleware's services.
[Registration](core-concepts.md#registration) has the rules every builder method follows.

### Startup validation

`app.UseTenantry()` throws `InvalidOperationException` when the pipeline is built if no resolver or no store is
registered. A web application that registers resolvers but never calls `app.UseTenantry()` fails to start, because no
request would have a tenant. A host that serves no requests (a worker) is not checked. Registering a second
[store](tenant-stores.md#registration-and-lifetimes) or [key type](core-concepts.md#the-tenant-key-tkey) throws at once.

## The middleware

```csharp
var app = builder.Build();
app.UseTenantry();
```

For each request, the middleware:

1. Takes the first identifier a resolver returns, in registration order
   ([Resolver ordering and fallback](tenant-resolution.md#resolver-ordering-and-fallback)).
2. If no resolver produced an identifier, rejects a request that
   [requires a tenant](access-control.md#requiring-a-tenant) (`400 Bad Request`), and continues any other without one.
3. Finds the tenant with `ITenantLookup<TKey>.FindByIdentifierAsync`
   ([lifetimes](tenant-stores.md#registration-and-lifetimes)). It calls your store's `FindByIdentifierAsync` (by
   default: parse the identifier as `TKey` and look the id up), and serves the tenant from the cache with
   [`CacheTenants`](tenant-stores.md#caching).
4. Runs the [access validators](access-control.md) in the order they were added, then checks the tenant is
   [active](tenant-stores.md#suspended-and-inactive-tenants). A caller the validators refuse is denied access whether or
   not the tenant is active, so only a caller they allow can learn that a tenant is suspended.
5. Makes the tenant current (`ITenantContextSetter.MakeCurrent`) for the rest of the request and raises
   [`OnResolved`](#events). It tags the request's trace span `tenant.id` and opens a log scope with `TenantId`. The
   tenant is restored when the request ends.

When step 3 finds no tenant, or step 4 refuses it, a request that requires a tenant is rejected
([Status codes](#status-codes)). Any other request continues without a tenant, so a `www.` host or a stale header does
not break your login and health endpoints. With `app.UseTenantResolution()` there is one exception: a signed-in request
whose tenant was current during authentication, and which the access validators refuse, is refused on every endpoint
([Authentication per tenant](authentication-per-tenant.md#how-the-two-steps-work)).

Resolvers and access validators added by type (`UseResolver<TResolver>()`, `ValidateTenantAccess<TValidator>()`) are
created in the request's service scope, so they can depend on a scoped `DbContext`.

### Status codes

| Situation (on an endpoint that requires a tenant) | Default status | Option |
|---------------------------------------------------|----------------|--------|
| No resolver produced an identifier | `400 Bad Request` | `MissingTenantStatusCode` |
| The identifier names no tenant (with the default lookup: it does not parse, is the key type's default, or is not in the store) | `404 Not Found` | `TenantNotFoundStatusCode` |
| An access validator refused the tenant | `403 Forbidden` | `AccessDeniedStatusCode` |
| The tenant is not active (`ValidateTenantActivity`) | `403 Forbidden`, with the access-denied response | `InactiveTenantStatusCode` |
| The identifier names no tenant, and access validators are configured | same as access denied | `AccessDeniedStatusCode` |

With access validators, a tenant that does not exist gets exactly the response of one the caller may not use. An
authenticated user of one tenant cannot probe for others. A suspended tenant gets that response too, unless you give
`InactiveTenantStatusCode` another value, such as `402 Payment Required` for a lapsed subscription. Even then, only
callers the access validators allow get it. Change the codes with `ConfigureResolution`:

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

A rejection's body is empty unless an `IProblemDetailsService` is registered (`builder.Services.AddProblemDetails()`).
Then it is `application/problem+json` with the status, a title (`Tenant required`, `Tenant not found`,
`Tenant access denied`) and a short detail. The body never repeats the identifier the request sent. Customise the
problem details as usual, with `AddProblemDetails(options => options.CustomizeProblemDetails = …)`, or write a
response of your own in [`OnRejected`](#events).

## Events

`ConfigureResolution` sets two handlers:

- `OnResolved` runs once a request's tenant is current, before the rest of the pipeline, with the request and the
  tenant: to add the tenant to your own telemetry, for example. To set ambient state such as the culture from it, do
  not make it `async` ([`OnResolved`](api/tenantry-aspnetcore-tenantresolutionoptions.md#onresolved)). For a per-tenant
  culture, use `UseRequestLocalization` with a culture provider that reads `ITenantContext<TKey>`. To refuse a tenant,
  use an [access validator](access-control.md#validating-tenant-access).
- `OnRejected` runs before Tenantry writes a rejection: on an endpoint that requires a tenant, and, with
  `app.UseTenantResolution()`, for the signed-in request [above](#the-middleware) refused on every endpoint. Its
  [context](api/tenantry-aspnetcore-tenantrejectedcontext.md) has the `Reason`, the `StatusCode` Tenantry would send,
  the `Identifier` the request sent and, for a refused tenant, the `Tenant`. Change `StatusCode`, or write your own
  response and call `HandleResponse()`, so Tenantry writes none:

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

`Reason` is the real reason, for your logs. `StatusCode` hides it as [Status codes](#status-codes) describes: keep it
hidden in a response of your own. `Identifier` is request input: encode it before you repeat it.

## Pipeline ordering

Call `UseTenantry()` after routing and `UseAuthentication()`, and before anything that needs the tenant, such as your
endpoints and the EF Core work they do. After routing, it sees endpoint metadata: `WebApplication` adds routing first,
and a custom pipeline must call `UseRouting()` before `UseTenantry()`. After authentication, the access validators and
`ResolveFromClaim` see the user.

Where `UseAuthorization()` goes depends on whether you use `UseTenantResolution()`:

- Without it, put `UseTenantry()` after `UseAuthorization()` too, so an anonymous caller gets 401 rather than 403. If an
  authorization policy needs the tenant, put it before `UseAuthorization()` instead.
- For authentication settings that differ per tenant, call `UseTenantResolution()` before `UseAuthentication()`. Then
  `UseAuthorization()` always comes after `UseTenantry()`, so no policy sees a tenant the access validators have not
  checked ([Authentication per tenant](authentication-per-tenant.md)).

A typical order, without `UseTenantResolution()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();   // first, so an anonymous caller gets 401 (not with UseTenantResolution())
app.UseTenantry();        // resolves the tenant (reads User if using claims; reads endpoint metadata)
app.MapControllers();     // or minimal API endpoints
```

[When the order is wrong](#when-the-order-is-wrong) has what each mistake does.

## Reading the tenant in your code

Inject `ITenantContext<TKey>` anywhere. On an endpoint that requires a tenant (`RequireTenant()`, or
`RequireTenantByDefault()`), a request without one is refused before the handler runs:

```csharp
app.MapGet("/me", (ITenantContext<Guid> ctx) => Results.Ok(ctx.RequiredTenant.Name))
    .RequireTenant();
```

[`RequiredTenant`](api/tenantry-itenantcontext.md) returns the current tenant, and throws when there is none. Check
`HasTenant` only on an endpoint that does not require a tenant. Read `CurrentTenantId` for work outside EF Core, which
applies the tenant for you.

## MVC / controllers

Everything above applies to controllers too. The endpoint metadata is `[RequireTenant]` and `[AllowMissingTenant]` on
controllers, and `.RequireTenant()` and `.AllowMissingTenant()` on minimal APIs ([Access control](access-control.md)).

## SignalR and Blazor Server

A SignalR connection takes its tenant from the request that opened it, which `UseTenantry()` resolves like any other.
The tenant then stays current for every hub method call on the connection, and for a Blazor Server circuit, which runs
over one. The negotiate request and the later requests of a long-polling connection do not change it.

Resolve the tenant from the host, a subdomain, a claim, or a route value in the hub's path
(`app.MapHub<ChatHub>("/{tenant}/chat")`). Do not resolve it from a header: a browser cannot set headers on a WebSocket
or Server-Sent Events connection. Blazor Server's connection is to `/_blazor`, with no tenant in the path, so a Blazor
Server app resolves from the host, a subdomain or a claim.

The middleware checks the tenant once, when the connection opens. To stop a tenant that is suspended or deleted while
its connections are open, add Tenantry's check to your hubs, and in an app with Blazor Server to its circuits:

```csharp
using Microsoft.AspNetCore.SignalR;

builder.Services.AddSignalR(options => options.AddTenantry());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddTenantry();
```

Before each hub method call, and each event, JavaScript interop call or navigation of a circuit, the check looks the
tenant up again, through the cache with [`CacheTenants`](tenant-stores.md#caching). It checks that the tenant is
[active](tenant-stores.md#suspended-and-inactive-tenants). A refused hub method call fails and does not run, and the
connection stays open ([`AddTenantry`](api/microsoft-aspnetcore-signalr-tenantryhuboptionsextensions.md)). A refused
circuit ends
([`AddTenantry`](api/microsoft-extensions-dependencyinjection-tenantryserversideblazorbuilderextensions.md)). With
`CacheTenants`, a change reaches open connections once the cached tenant expires or `ITenantInvalidator` removes it.
The descriptor that is current does not change: a connection sees other changes to its tenant when it reconnects.

## Details

### When the order is wrong

- If the middleware runs before routing, a request without a tenant to an endpoint that requires one is still
  rejected. But `RequireTenantByDefault()` then applies to `AllowMissingTenant()` endpoints too, and route values are
  not there to resolve from. The middleware logs this once ([event 1007](diagnostics.md#logs)).
- If the authentication middleware runs after it, `ResolveFromClaim` sees no user. The middleware logs this once
  (event 1008).
- A user signed in by other code after the middleware, such as authorization with a scheme that is not the default, is
  not seen by `ResolveFromClaim` either. Nothing warns of it: make that scheme the default.
