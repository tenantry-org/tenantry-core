# Tenant resolution

Resolution is the act of extracting a raw tenant identifier from an HTTP request. A resolver
implements `ITenantResolver`:

```csharp
public interface ITenantResolver
{
    ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken ct = default);
}
```

A resolver returns the raw id **as a string**, or `null` if it cannot determine the tenant from this
request. The middleware then parses that string into `TKey` and looks it up in the store.

> Resolution is an **ASP.NET Core** concept. In console/worker apps there is no `HttpContext`; you set
> the tenant directly with `BeginScope` — see [Non-HTTP hosts](non-http-hosts.md).

## Built-in resolvers

| Method | Source | Notes |
|--------|--------|-------|
| `ResolveFromHeader(name)` | request header `name` | Trims whitespace. e.g. `X-Tenant-Id`. |
| `ResolveFromSubdomain()` | first host segment | Requires ≥3 dot-separated segments; see below. |
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
tenant.ResolveFromSubdomain();   // acme.app.example.com → "acme"
```

To distinguish a real subdomain from a bare domain, the host must have **at least three** dot-separated
segments. So `acme.app.example.com` resolves to `acme`, but `app.example.com`, `example.com`,
`localhost`, and `acme.localhost` all resolve to `null`. For local development, use header resolution
instead.

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

At least one resolver must be registered, or `AddTenantry` throws at startup.

## Custom resolvers

Implement `ITenantResolver` for any source not covered above — a cookie, a gRPC metadata entry, a
combination of signals, an external lookup, etc.

```csharp
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
tenant.UseResolver<CookieTenantResolver>();                          // resolved from DI (singleton)
tenant.UseResolver(new CookieTenantResolver());                      // a specific instance
tenant.UseResolver(sp => new CookieTenantResolver(/* deps */));      // via a factory
```

Registration order relative to the built-in resolvers is preserved, so you can slot a custom resolver
anywhere in the fallback chain.

Return only a raw identifier — do **not** validate the tenant exists; that is the store's job, and
returning a value the store does not know yields a clean `404`.
