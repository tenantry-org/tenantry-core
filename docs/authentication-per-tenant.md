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

`ConfigureAll<TOptions>` sets every scheme of a type at once. The tenant's settings apply before the handler's own
post-configuration, so the scheme's metadata manager and data protector are built from them
([`Configure`](api/tenantry-options-tenantoptionsbuilder.md)).

Then resolve the tenant before authentication, check it after, and authorize last:

```csharp
var app = builder.Build();

app.UseTenantResolution();   // finds the tenant and makes it current
app.UseAuthentication();     // authenticates with the tenant's settings
app.UseTenantry();           // runs the access validators, then rejects or continues
app.UseAuthorization();      // sees only a tenant the validators allowed
```

Call `app.UseAuthentication()` and `app.UseAuthorization()` yourself. The ones `WebApplication` adds on its own run
before your middleware, so authentication would use no tenant's settings (event 1010 warns of this).

Authorize after `app.UseTenantry()`. Before it, an authorization policy that reads the tenant would see the one the
request names before the validators refuse it, and could let the caller in. With this order, an anonymous caller to an
endpoint that requires a tenant gets the tenant's rejection (`403` when an access validator refuses it), not `401`.

Put only `app.UseAuthentication()` between `app.UseTenantResolution()` and `app.UseTenantry()`
([Checks on the pipeline](#checks-on-the-pipeline)).

## How the two steps work

`app.UseTenantResolution()` runs the resolvers, looks the tenant up and checks it is
[active](tenant-stores.md#suspended-and-inactive-tenants), then makes it current. A suspended tenant is not made
current, so authentication uses the default settings. It cannot read the user, so it stops at the first resolver that
needs one, a claim resolver or `ResolveFromPropagationHeader`: only the resolvers added before it run here.

`app.UseTenantry()` then runs the [access validators](access-control.md#validating-tenant-access). It runs them on a
suspended tenant too, so a caller they refuse is denied access whether or not the tenant is suspended. If nothing
resolved before authentication, it runs every resolver again, in order, so a resolver added after a claim resolver
never wins over the claim. Endpoints that require a tenant are rejected as usual.

Add the resolver that names the tenant for authentication (host, subdomain, route or header) before any claim
resolver. A tenant resolved after authentication, by a claim or by a resolver added after one, authenticates with the
default settings. A route value exists only once routing has run. `WebApplication` adds routing first, and a pipeline
that calls `app.UseRouting()` itself must call it before `app.UseTenantResolution()` (event 1016 warns otherwise).

Between the two, the tenant is current but not yet checked against the user:

- Authentication events (`OnTokenValidated`, `OnValidatePrincipal`) and claims transformations
  (`IClaimsTransformation`) run with the tenant current before it is checked. They must not grant claims, roles or
  permissions from the current tenant, and must not write as it: the caller may not be allowed to use it.
- Do not renew or reissue a cookie with claims taken from the current tenant either, even one the caller may use. A
  cookie that is valid on several tenants carries those claims to the others.
- If the validators refuse a tenant that was current during authentication, a request with a signed-in user gets the
  access-denied response on every endpoint. A signed-in user here is an authenticated identity, or any claims. The
  user was authenticated as a tenant it may not use. Tenantry also [signs out](#what-a-refusal-signs-out) the cookie
  schemes on that request.
- A caller with no identity and no claims, such as an anonymous one, is handled as without early resolution. The
  tenant is not current, an endpoint that requires one is rejected, and one that does not runs.
- An endpoint the request reaches without passing `app.UseTenantry()`, such as one in a pipeline branch, does not run:
  it gets `500` and log event 1011.
- An application with `app.UseTenantResolution()` and no `app.UseTenantry()` fails to start.

## Sign-in redirects

OpenID Connect's sign-in returns to your application through the identity provider (`/signin-oidc`), with only what
the URL carries, so resolve the tenant from the host, subdomain or route. A tenant named by a header cannot sign in
this way, as the identity provider's redirect does not send the header.

## Cookies

Put the tenant in the cookie's ticket and check it:

- add a `tenant_id` claim when you sign the user in, and
- add `tenant.ValidateTenantAccessByClaim("tenant_id")`, or a validator of your own that compares them.

Every tenant's cookies are protected with the application's one key ring. A cookie issued for one tenant also decrypts
for another, even under another cookie name, so a per-tenant name does not stop it being replayed.

Name the cookie per tenant whenever your tenants' hosts share cookies (a cookie domain such as `.example.com`). Each
tenant's handler then reads, renews and deletes only its own cookie. A user signed in to one tenant is then anonymous
on another and can use its sign-in page (the table below). The identifier must be valid in a cookie name.

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

With one name shared across subdomains, a cookie replayed on another tenant is refused there with `403`, since its
user names a tenant the validator refuses. That covers every page after `app.UseTenantry()`, the sign-in page and
other `AllowMissingTenant()` pages included, until the user signs out. Tenantry signs every cookie scheme out on that
request, whether the cookie scheme or a remote scheme over it is the default:

| Cookie set-up | On the other tenant | Back on their own tenant |
|---|---|---|
| A name per tenant | anonymous; nothing is refused, renewed or deleted | signed in |
| One name, ticket in the cookie | `403`; the response sets no cookie | signed in: the browser keeps its cookie, whose ticket was never changed |
| One name, `SessionStore` | `403`; the session is removed from the store | signed out: the cookie names a session that no longer exists |

Static files that `app.UseStaticFiles()` serves can go before `app.UseTenantResolution()`, out of the refusal's reach
([Static files](#static-files)), but a sign-in page cannot. That is why a cookie name per tenant is the recommended
set-up.

The validator refuses an anonymous caller too, but such a caller carries no claims, so a sign-in endpoint runs with no
tenant current. Mark it `AllowMissingTenant()` and take the tenant from the request, such as its host, when you issue
the cookie. Its authentication handler was created with the tenant current, so it writes the tenant's cookie. It must
stay after `app.UseTenantResolution()` for that.

### Static files

Static files that `app.UseStaticFiles()` serves before `app.UseTenantResolution()` are out of the refusal's reach and
need no tenant. Assets that `app.MapStaticAssets()` maps, as the .NET 9 and 10 templates do, are endpoints, which run
after `app.UseTenantry()`. A refusal covers them as it covers every page. With
[`RequireTenantByDefault()`](access-control.md#requiring-a-tenant), a request for one that names no tenant is rejected
unless you map them with `app.MapStaticAssets().AllowMissingTenant()`.

## Identity provider metadata

JWT bearer and OpenID Connect fetch the provider's metadata (its signing keys) and cache it in their options. With
per-tenant options, each tenant has its own copy, fetched on its first request. Invalidating the tenant
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

## Details

### Checks on the pipeline

Authorization between `app.UseTenantResolution()` and `app.UseTenantry()` runs before the access validators check the
tenant, so a policy that reads the tenant could let in a caller who may not use it. Two checks catch it there:

- The application fails to start if `app.UseAuthorization()` is between them.
- If the authorization middleware is added there some other way, each request it runs for gets `500` and log event
  1013.

Both read keys ASP.NET Core sets but does not document. If a version stops setting them, the application logs event
1014 as it starts, and the checks no longer catch anything. They cannot see:

- middleware of your own between the two that calls `IAuthorizationService` itself (an endpoint filter that does
  runs after `app.UseTenantry()`, so it sees only a checked tenant), and
- a fallback policy evaluated for a request with no endpoint, which the authorization middleware does not mark.

### What a refusal signs out

On the refusal of a signed-in user [above](#how-the-two-steps-work):

- Tenantry signs out each scheme that signs out locally: each cookie scheme, Identity's external and two-factor
  cookies included. So nothing renews the user, and a `SessionStore` drops the session.
- That covers the cookie under a remote default scheme, such as OpenID Connect set up by `AddMicrosoftIdentityWebApp`.
- Remote schemes are not signed out, as that would start a sign-out at the identity provider. Nor are policy schemes.
  JWT bearer has nothing to sign out.
- A sign-out that fails logs event 1015, and the others still run.
- Each such scheme is signed out every time, even one the user never signed in with. So your `OnSigningOut` handlers
  run on these refusals too: do not treat them as a logout the user asked for.
- The response sets none of the cookies set after `app.UseTenantResolution()`, the sign-outs' deletions included, and
  no sign-out redirect.
- Middleware before `app.UseTenantResolution()` must not store `HttpContext.User` as the response starts. Its
  `OnStarting` callbacks run after Tenantry's, so they see the refused user.

## See also

- [Options per tenant](per-tenant-options.md)
- [Access control](access-control.md)
- [ASP.NET Core integration](aspnetcore-integration.md#pipeline-ordering)
