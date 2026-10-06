# Authentication per tenant

Tenants often sign in with settings of their own: their own identity provider (`Authority`), client id, audience or
cookie name. ASP.NET Core's authentication handlers read these per request as named options
(`IOptionsMonitor<TOptions>.Get(scheme)`), so with `Tenantry.Options` each scheme's settings can differ by tenant while
the schemes stay the same.

```bash
dotnet add package Tenantry.Options
```

## Setup

Register the scheme as usual, with the defaults, then set what differs per tenant in `ConfigurePerTenant`, naming
the scheme:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Tenantry;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.Audience = "api");

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .ConfigurePerTenant(perTenant => perTenant
        .Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (o, t) => o.Authority = t.As<AppTenant>().Authority)));
```

`ConfigureAll<TOptions>` sets every scheme of a type at once. The tenant's settings apply after every
`Configure` and before the handler's own post-configuration, which builds the scheme's metadata manager and data
protector from them.

Then resolve the tenant before authentication, check it after, and authorize last:

```csharp
var app = builder.Build();

app.UseTenantResolution();   // finds the tenant and makes it current
app.UseAuthentication();     // authenticates with the tenant's settings
app.UseTenantry();           // runs the access validators, then rejects or continues
app.UseAuthorization();      // sees only a tenant the validators allowed
```

Call `app.UseAuthentication()` and `app.UseAuthorization()` yourself: the ones `WebApplication` adds on its own run
before your middleware, so authentication would use no tenant's settings (event 1010 warns of this).

Authorization comes after `app.UseTenantry()` here, so an anonymous caller to an endpoint that requires a tenant gets
the tenant's rejection (`403` when an access validator refuses it) rather than `401`. Before `app.UseTenantry()`, an
authorization policy that reads the tenant would see the one the request names before the validators refuse it, and
could let the caller in.

Put only `app.UseAuthentication()` between `app.UseTenantResolution()` and `app.UseTenantry()`. Two checks catch the
usual mistake: the application fails to start if `app.UseAuthorization()` is between them, and if the authorization
middleware is added there some other way, each request it runs for gets `500` and log event 1013. Both read keys ASP.NET
Core sets but does not document; if a version stops setting them, the application logs event 1014 as it starts, and the
checks no longer catch anything. They cannot see:

- middleware of your own between the two that calls `IAuthorizationService` itself (an endpoint filter that does
  runs after `app.UseTenantry()`, so it sees only a checked tenant), and
- a fallback policy evaluated for a request with no endpoint, which the authorization middleware does not mark.

## How the two steps work

`app.UseTenantResolution()` runs the resolvers, looks the tenant up and checks it is
[active](tenant-stores.md#suspended-and-inactive-tenants), then makes it current; a suspended tenant is not made
current, so authentication uses the default settings. It cannot read the user, so it stops at the first resolver that
needs one, a claim resolver or `ResolveFromPropagationHeader`: only the resolvers added before it run here.
`app.UseTenantry()` then runs the [access validators](access-control.md#validating-tenant-access), on a suspended tenant
too, so a caller they refuse is denied access whether or not the tenant is suspended. If nothing resolved before
authentication, it runs every resolver again, in order, so a resolver added after a claim resolver never wins over the
claim. Endpoints that require a tenant are rejected as usual.

Between the two, the tenant is current but not yet checked against the user:

- Authentication events (`OnTokenValidated`, `OnValidatePrincipal`) and claims transformations
  (`IClaimsTransformation`) run with the tenant current before it is checked. They must not grant claims, roles or
  permissions from the current tenant, and must not write as it: the caller may not be allowed to use it. Do not
  renew or reissue a cookie with claims taken from the current tenant either, even one the caller may use: a cookie
  that is valid on several tenants carries those claims to the others.
- If the validators refuse a tenant that was current during authentication, and the request has a signed-in user (an
  authenticated identity, or any claims), the request gets the access-denied response on every endpoint: the user was
  authenticated as a tenant it may not use.
- On that refusal, Tenantry signs out each scheme that signs out locally, which means each cookie scheme, Identity's
  external and two-factor cookies included, so nothing renews the user and a `SessionStore` drops the session. That
  covers the cookie under a remote default scheme, such as OpenID Connect set up by `AddMicrosoftIdentityWebApp`. Remote
  schemes are not signed out, as that would start a sign-out at the identity provider, nor are policy schemes; JWT
  bearer has nothing to sign out. A sign-out that fails logs event 1015, and the others still run. Each such scheme is
  signed out every time, even one the user never signed in with, so your `OnSigningOut` handlers run on these refusals
  too: do not treat them as a logout the user asked for. The response sets none of the cookies set after
  `app.UseTenantResolution()`, the sign-outs' deletions included, and no sign-out redirect.
- A caller with no identity and no claims, such as an anonymous one, is handled as without early resolution: the
  tenant is not current, an endpoint that requires one is rejected, and one that does not runs.
- Middleware before `app.UseTenantResolution()` must not store `HttpContext.User` as the response starts: its
  `OnStarting` callbacks run after Tenantry's, so they see the refused user.
- An endpoint the request reaches without passing `app.UseTenantry()`, such as one in a pipeline branch, does not run:
  it gets `500` and log event 1011.
- An application with `app.UseTenantResolution()` and no `app.UseTenantry()` fails to start.

Add the resolver that names the tenant for authentication (host, subdomain, route or header) before any claim
resolver: a tenant resolved after authentication, by a claim or by a resolver added after one, authenticates with the
default settings. A route value exists only once routing has run: `WebApplication` adds routing first, and a pipeline
that calls `app.UseRouting()` itself must call it before `app.UseTenantResolution()` (event 1016 warns otherwise).

## Sign-in redirects

OpenID Connect's sign-in returns to your application through the identity provider (`/signin-oidc`), with only what
the URL carries, so resolve the tenant from the host, subdomain or route. A tenant named by a header cannot sign in
this way, as the identity provider's redirect does not send the header.

## Cookies

Every tenant's cookies are protected with the application's one key ring, so a cookie issued for one tenant also
decrypts for another, even under another cookie name, and a per-tenant name does not stop it being replayed. Put the
tenant in the ticket and check it:

- add a `tenant_id` claim when you sign the user in, and
- add `tenant.ValidateTenantAccessByClaim("tenant_id")`, or a validator of your own that compares them.

Name the cookie per tenant whenever your tenants' hosts share cookies (a cookie domain such as `.example.com`). Each
tenant's handler then reads, renews and deletes only its own cookie, so on another tenant a signed-in user is
anonymous: that tenant's sign-in page works, nothing is refused, and their own session is untouched. The identifier
must be valid in a cookie name.

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Tenantry;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .ValidateTenantAccessByClaim("tenant_id")
    .ConfigurePerTenant(perTenant => perTenant
        .Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, (o, t) =>
            o.Cookie.Name = $".App.{t.TenantId}")));
```

Without a name per tenant, a cookie replayed on another tenant is refused there, on every endpoint, since its user
names a tenant the validator refuses. Tenantry signs every cookie scheme out on that request, whether the cookie scheme
or a remote scheme over it is the default:

| Cookie set-up | On the other tenant | Back on their own tenant |
|---|---|---|
| A name per tenant | anonymous; nothing is refused, renewed or deleted | signed in |
| One name, ticket in the cookie | `403`; the response sets no cookie | signed in: the browser keeps its cookie, whose ticket was never changed |
| One name, `SessionStore` | `403`; the session is removed from the store | signed out: the cookie names a session that no longer exists |

The validator refuses an anonymous caller too, but such a caller carries no claims, so a sign-in endpoint runs with no
tenant current: mark it `AllowMissingTenant()` and take the tenant from the request, such as its host, when you issue
the cookie. Its authentication handler was created with the tenant current, so it writes the tenant's cookie, and it
must stay after `app.UseTenantResolution()` for that.

With one cookie name shared across subdomains, a user signed in to one tenant is refused with `403` on every page of
another after `app.UseTenantry()`, its sign-in page and other `AllowMissingTenant()` pages included, until they sign
out. Static files can go before `app.UseTenantResolution()`; a sign-in page cannot, which is why a cookie name per
tenant is the recommended set-up.

## Identity provider metadata

JWT bearer and OpenID Connect fetch the provider's metadata (its signing keys) and cache it in their options, so with
per-tenant options each tenant has its own copy, fetched on its first request. Invalidating the tenant
(`ITenantInvalidator<TKey>.InvalidateAsync`) clears it with the tenant's other options.

## A scheme per tenant

Tenants on different identity providers, such as one on Entra ID and another on Google, need a scheme each. Register
one per provider, and make the default a policy scheme that forwards to the current tenant's. It runs during
authentication, after `app.UseTenantResolution()` has made the tenant current:

```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Tenantry;

builder.Services.AddAuthentication("tenant")
    .AddPolicyScheme("tenant", "The tenant's provider", o => o.ForwardDefaultSelector = http =>
        http.RequestServices.GetRequiredService<ITenantContext<Guid>>().GetCurrentTenant<AppTenant>()?.SignInScheme
        ?? "entra")
    .AddOpenIdConnect("entra", o => o.CallbackPath = "/signin-entra")
    .AddOpenIdConnect("google", o => o.CallbackPath = "/signin-google");
```

Give each scheme a callback path of its own. Each can still take settings per tenant, with
`Configure<OpenIdConnectOptions>("entra", …)` in `ConfigurePerTenant`. The schemes themselves are registered at startup,
so a tenant on a new provider needs a scheme added and a restart.

## See also

- [Options per tenant](per-tenant-options.md)
- [Access control](access-control.md)
- [ASP.NET Core integration](aspnetcore-integration.md#pipeline-ordering)
