# Tenant resolution

Resolution turns an HTTP request into a tenant, in two steps:

1. A resolver reads an identifier from the request: a header, the subdomain, the host name, a route value, a
   claim. A resolver implements `ITenantResolver`:

   ```csharp no-compile
   public interface ITenantResolver
   {
       ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken ct = default);
   }
   ```

   It returns the identifier as a string, or `null` (or an empty string) if the request does not carry one.
2. The tenant store finds the tenant the identifier names, with `ITenantStore<TKey>.FindByIdentifierAsync`.
   By default an identifier is the tenant's id: it is parsed as `TKey` (with the invariant culture) and looked up
   with `GetTenantAsync`. A store can map other names to its tenants, such as a slug or a custom domain for
   `Guid`-keyed tenants: see [Identifiers other than the id](#identifiers-other-than-the-id).

Resolution is part of `Tenantry.AspNetCore`. In console and worker apps there is no request: you make a tenant
current with `ITenantScopeFactory` ([Non-HTTP hosts](non-http-hosts.md)).

## Built-in resolvers

| Method | Source | Notes |
|--------|--------|-------|
| `ResolveFromHeader(name)` | request header `name` | Trims whitespace. A repeated header names no tenant. e.g. `X-Tenant-Id`. |
| `ResolveFromSubdomain(options)` | the subdomain of the host | Ignores `www` and IP addresses; takes base domains. See below. |
| `ResolveFromHost()` | the host name | For tenants with domains of their own. See below. |
| `ResolveFromRouteValue(key = "tenant")` | route value `key` | For routes like `/api/{tenant}/…`. Needs routing before the middleware. |
| `ResolveFromClaim(type = "tenant_id")` | claim on `HttpContext.User` | Needs authentication before the middleware. |
| `ResolveFromQueryString(name = "tenantId")` | query string parameter | Development and testing only: see [Query string](#query-string). |
| `ResolveFromPropagationHeader(isTrustedCaller)` | the `tenantry-tenant-id` header another service sent | Only from a caller `isTrustedCaller` accepts, after authentication; read as a tenant id. See [Calling other services](http-propagation.md). |

### Header

```csharp
tenant.ResolveFromHeader("X-Tenant-Id");
```

The most common choice for APIs and service-to-service calls. The value is trimmed; an empty value, or a header
sent more than once, names no tenant. A proxy that sets the header must replace the client's, not add a second one.

### Subdomain

```csharp
tenant.ResolveFromSubdomain(options =>
{
    options.BaseDomains.Add("example.com");  // acme.example.com → "acme"
    options.BaseDomains.Add("localhost");    // acme.localhost → "acme", in development
    options.IgnoredSubdomains.Add("api");    // api.example.com serves the app itself; "www" is ignored by default
});
```

With base domains set, only a host of exactly one label followed by a base domain resolves a tenant:
`acme.example.com` resolves to `acme`, while `example.com`, `www.example.com`, `x.acme.example.com` and
`acme.other.org` resolve nothing. Add `localhost` for development, so `acme.localhost` resolves to `acme`
(browsers send `*.localhost` to the local machine).

Without it (`tenant.ResolveFromSubdomain()`), the resolver takes the first label of any host that has at least
three, and resolves nothing for shorter hosts: `acme.app.example.com` and `app.example.com` resolve to `acme`
and `app`, while `example.com`, `localhost` and `acme.localhost` resolve nothing.

Either way, a subdomain in `IgnoredSubdomains` (`www` by default) and a host that is an IP address (a load
balancer's or Kubernetes' health probe) resolve nothing, and the subdomain is returned in lower case (host names are
compared without regard to case). A host that still names no tenant does not break an endpoint that does not require
one: the request continues without a tenant (see [ASP.NET Core integration](aspnetcore-integration.md#the-middleware)).

### Host (custom domains)

```csharp
tenant.ResolveFromSubdomain(options => options.BaseDomains.Add("example.com"));     // acme.example.com → "acme"
tenant.ResolveFromHost(options => options.ExcludedDomains.Add("example.com"));      // app.acme.com → "app.acme.com"
```

`ResolveFromHost()` returns the request's host name, in lower case and without a port, for tenants that bring
their own domain; your store's `FindByIdentifierAsync` maps the host name to a tenant. A host that is an IP address,
or is or is under one of `ExcludedDomains` (`localhost` by default), resolves nothing.

Every other host resolves, so a resolver added after it never runs for those hosts: add it last. Exclude your own
domain, as above: otherwise its hosts that are not tenants (`www.example.com`, `example.com`, `api.example.com`) each
ask the store for a tenant on every request, since a lookup that finds none is not [cached](tenant-stores.md#caching).

Both host resolvers compare and return an international domain name in its ASCII form (`xn--…`), as DNS and
certificates have it: ASP.NET Core decodes it to Unicode, and the resolvers encode it again.

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

Reads the claim from `HttpContext.User`, so `UseTenantry()` must come after `UseAuthentication()`. The caller
cannot name a tenant its token does not carry.

### Query string

```csharp
tenant.ResolveFromQueryString();         // ?tenantId=acme
tenant.ResolveFromQueryString("tenant"); // ?tenant=acme
```

> Do not use this resolver in production: query strings are logged, cached by CDNs and stored in browser history.
> It is for local development and tests.

## Resolver ordering and fallback

You can register several resolvers. The middleware tries them in registration order and uses the
first identifier one returns (`null`, an empty string or whitespace counts as none):

```csharp
tenant.ResolveFromClaim("tenant_id");             // 1. the tenant in the caller's token
tenant.ResolveFromHeader("X-Tenant-Id");          // 2. otherwise, the header
tenant.ValidateTenantAccessByClaim("tenant_id");  // the caller must be entitled to it
```

Put the most trusted source first. If none match, the request proceeds without a tenant unless a tenant is
required (see [Access control](access-control.md)).

- `ResolveFromClaim` reads the first matching claim, so use it only for tokens that carry exactly one tenant. For
  tokens that list several, resolve from the header and validate against the claims.
- A header fallback lets any caller without the claim, anonymous ones too, name a tenant. Pair it with an access
  validator such as `ValidateTenantAccessByClaim`.

At least one resolver must be registered, or `app.UseTenantry()` throws at startup.

## Custom resolvers

Implement `ITenantResolver` for another source, such as a cookie, a gRPC metadata entry or a combination of
signals.

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
tenant.UseResolver(sp => new CookieTenantResolver(/* deps */));      // by a factory, in each request's scope
tenant.UseResolver<CookieTenantResolver>();                          // created in each request's scope
```

`UseResolver<TResolver>()` and the factory overload create the resolver in each request's scope, so it can depend on
scoped services such as a `DbContext`. Like `ValidateTenantAccess<T>()`, `UseResolver<TResolver>()` returns the
builder without its key type ([Registration](core-concepts.md#registration)). The request's scope disposes a resolver
the factory returns, so return a new one. An instance is used for every request.

A custom resolver runs in the order it was added among the built-in ones.

Return only the identifier, not the tenant: the store looks it up. An identifier the store does not know is rejected
on an endpoint that requires a tenant (`404`, or `403` when access validators are configured).

## Identifiers other than the id

With `Guid` or `int` keys, a subdomain, a slug in a route or a custom domain is not the tenant's id. Implement
`FindByIdentifierAsync` in your store to map it: the middleware, and `ITenantLookup<TKey>`, call it with the
identifier a resolver returned.

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public sealed class AppTenantStore(CatalogDbContext db) : ITenantStore<Guid>
{
    public async ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Tenants.FindAsync([tenantId], ct);

    public async ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        await db.Tenants.ToListAsync(ct);

    // acme.example.com resolves "acme"; app.acme.com resolves "app.acme.com".
    public async ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken ct = default) =>
        await db.Tenants.SingleOrDefaultAsync(t => t.Slug == identifier || t.CustomDomain == identifier, ct);
}
```

Without it, the default parses the identifier as the key type and looks it up by id, so a store keyed by the
identifier itself (a `string` slug as the id) needs nothing more. To serve every request without a database
round trip, cache the lookups with [`CacheTenants`](tenant-stores.md#caching).
