# Tenant resolution

Resolution is the act of extracting a raw tenant identifier from an HTTP request. A resolver
implements `ITenantResolver`:

```csharp no-compile
public interface ITenantResolver
{
    ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken ct = default);
}
```

A resolver returns the raw id **as a string**, or `null` if it cannot determine the tenant from this
request. The middleware then parses that string into `TKey` and looks it up in the store.

> Resolution is an **ASP.NET Core** concept. In console/worker apps there is no `HttpContext`; you make a
> tenant current with `ITenantScopeFactory` — see [Non-HTTP hosts](non-http-hosts.md).

## Built-in resolvers

| Method | Source | Notes |
|--------|--------|-------|
| `ResolveFromHeader(name)` | request header `name` | Trims whitespace. e.g. `X-Tenant-Id`. |
| `ResolveFromSubdomain(options)` | the subdomain of the host | Ignores `www` and IP addresses; takes a base domain. See below. |
| `ResolveFromRouteValue(key = "tenant")` | route value `key` | For routes like `/api/{tenant}/…`. Needs routing before the middleware. |
| `ResolveFromClaim(type = "tenant_id")` | claim on `HttpContext.User` | Needs authentication before the middleware. |
| `ResolveFromQueryString(name = "tenantId")` | query string parameter | **Development/testing only** — see warning. |

### Header

```csharp
tenant.ResolveFromHeader("X-Tenant-Id");
```

The most common choice for APIs and service-to-service calls. The value is trimmed; empty/whitespace
yields `null`.

### Subdomain

```csharp
tenant.ResolveFromSubdomain(options =>
{
    options.BaseDomain = "example.com";      // acme.example.com → "acme"
    options.IgnoredSubdomains.Add("api");    // api.example.com serves the app itself; "www" is ignored by default
});
```

With `BaseDomain` set, only a host of exactly one label followed by the base domain resolves a tenant:
`acme.example.com` resolves to `acme`, while `example.com`, `www.example.com`, `x.acme.example.com` and
`acme.other.org` resolve nothing. In development, set it to `localhost` so `acme.localhost` resolves to `acme`
(browsers send `*.localhost` to the local machine).

Without it (`tenant.ResolveFromSubdomain()`), the resolver takes the first label of any host that has **at least
three**, and resolves nothing for shorter hosts: `acme.app.example.com` and `app.example.com` resolve to `acme`
and `app`, while `example.com`, `localhost` and `acme.localhost` resolve nothing.

Either way, a subdomain in `IgnoredSubdomains` (`www` by default) and a host that is an IP address (a load
balancer's or Kubernetes' health probe) resolve nothing. A host that still names no tenant does not break an
endpoint that does not require one: the request continues without a tenant (see
[ASP.NET Core integration](aspnetcore-integration.md#the-middleware)).

### Route value

```csharp
tenant.ResolveFromRouteValue();          // default key "tenant": /api/{tenant}/orders
tenant.ResolveFromRouteValue("org");     // /api/{org}/orders
```

Because this reads a route value, routing must have run before `UseTenantry()` (automatic with
`WebApplication`).

### Claim

```csharp
tenant.ResolveFromClaim();               // default claim type "tenant_id"
tenant.ResolveFromClaim("org_id");
```

Reads the claim from `HttpContext.User`, so `UseTenantry()` must come **after** `UseAuthentication()`.
This binds the tenant to the authenticated identity, which is the most tamper-resistant source — the
caller cannot choose a tenant they were not issued.

### Query string

```csharp
tenant.ResolveFromQueryString();         // ?tenantId=acme
tenant.ResolveFromQueryString("tenant"); // ?tenant=acme
```

> **Do not use in production.** Query strings are logged, cached by CDNs, and stored in browser
> history. This resolver exists for local development and testing convenience only.

## Resolver ordering and fallback

You can register several resolvers. The middleware tries them **in registration order** and uses the
**first non-null** result:

```csharp
tenant.ResolveFromClaim("tenant_id");     // 1. prefer the authenticated identity
tenant.ResolveFromHeader("X-Tenant-Id");  // 2. fall back to an explicit header
```

Order by trust and specificity: put the most authoritative source first. If none match, the request
proceeds without a tenant unless a tenant is required (see [Access control](access-control.md)).

At least one resolver must be registered, or `app.UseTenantry()` throws at startup.

## Custom resolvers

Implement `ITenantResolver` for any source not covered above — a cookie, a gRPC metadata entry, a
combination of signals, an external lookup, etc.

```csharp
using Tenantry.AspNetCore;

public sealed class CookieTenantResolver : ITenantResolver
{
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        var value = context.Request.Cookies["tenant"];
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
```

Register it by type, instance, or factory:

```csharp
tenant.UseResolver(new CookieTenantResolver());                      // a specific instance
tenant.UseResolver(sp => new CookieTenantResolver(/* deps */));      // via a factory
tenant.UseResolver<CookieTenantResolver>();                          // resolved from DI (singleton)
```

`UseResolver<TResolver>()` has a type parameter of its own, so it returns the builder without its key type: call
it last in a chain.

Registration order relative to the built-in resolvers is preserved, so you can slot a custom resolver
anywhere in the fallback chain.

Return only a raw identifier — do **not** validate the tenant exists; that is the store's job, and
returning a value the store does not know yields a clean `404` on an endpoint that requires a tenant.
