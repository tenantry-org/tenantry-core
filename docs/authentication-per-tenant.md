# Authentication per tenant

Tenants often sign in with settings of their own: their own identity provider (`Authority`), client id, audience or
cookie name. ASP.NET Core's authentication handlers read these per request, as named options
(`IOptionsMonitor<TOptions>.Get(scheme)`), so with `Tenantry.Options` each scheme's settings can differ by tenant
while the schemes themselves stay the same.

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

Then resolve the tenant before authentication, and check it after:

```csharp
var app = builder.Build();

app.UseTenantResolution();   // finds the tenant and makes it current
app.UseAuthentication();     // authenticates with the tenant's settings
app.UseTenantry();           // runs the access validators, then rejects or continues
app.UseAuthorization();
```

Call `app.UseAuthentication()` yourself: the one `WebApplication` adds on its own runs before your middleware, so it
would authenticate with no tenant's settings (event 1010 warns of this).

## How the two steps work

`app.UseTenantResolution()` runs the resolvers, looks the tenant up and checks it is
[active](tenant-stores.md#suspended-and-inactive-tenants), then makes it current. It cannot read the user, so it stops
at the first resolver that needs one, a claim resolver or `ResolveFromPropagationHeader`: only the resolvers added
before it run here. `app.UseTenantry()` then runs the
[access validators](access-control.md#validating-tenant-access). If nothing resolved before authentication, it runs
every resolver again, in order, so a resolver added after a claim resolver never wins over the claim. Endpoints that
require a tenant are rejected as usual.

Between the two, the tenant is current but not yet checked against the user. So:

- Put only `app.UseAuthentication()` between them.
- A tenant the validators refuse is not current for the rest of the request.
- An endpoint the request reaches without passing `app.UseTenantry()` (in a branch, say) does not run: it gets `500`
  and log event 1011.
- An application with `app.UseTenantResolution()` and no `app.UseTenantry()` fails to start.

Add the resolver that names the tenant for authentication (host, subdomain, route or header) before any claim
resolver. A tenant resolved after authentication, by a claim or by a resolver added after one, authenticated with the
default settings.

## Sign-in redirects

OpenID Connect's sign-in returns to your application through the identity provider (`/signin-oidc`). That request
carries only what the URL carries, so it must resolve to the same tenant: resolve from the host, subdomain or route.
A tenant named by a header cannot sign in this way, because the identity provider's redirect does not send the header.

## Cookies

Every tenant's cookies are protected with the application's one key ring, so a cookie issued for one tenant also
decrypts for another, even under another cookie name. A per-tenant cookie name does not stop it being replayed. Put
the tenant in the ticket and check it:

- add a `tenant_id` claim when you sign the user in, and
- add `tenant.ValidateTenantAccessByClaim("tenant_id")`, or a validator of your own that compares them.

The validator refuses an anonymous caller, so a sign-in endpoint runs with no tenant current: mark it
`AllowMissingTenant()` and take the tenant from the request (its host, say) when you issue the cookie. Its
authentication handler was created with the tenant current, so it writes the tenant's cookie.

## Identity provider metadata

JWT bearer and OpenID Connect fetch the provider's metadata (its signing keys) and cache it in their options. With
per-tenant options, each tenant has its own copy, fetched on its first request. Invalidating the tenant
(`ITenantInvalidator<TKey>.InvalidateAsync`) clears it with the tenant's other options.

## A scheme per tenant

Tenants on different identity providers, one on Entra ID and another on Google, say, need a scheme each. Register one
per provider, and make a policy scheme the default that forwards to the current tenant's. It runs during
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
