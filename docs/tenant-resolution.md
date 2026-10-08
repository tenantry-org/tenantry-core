# Tenant resolution

Resolution turns an HTTP request into a tenant, in two steps:

1. A resolver reads an identifier from the request: a header, the subdomain, the host name, a route value, a claim.
2. The tenant store finds the tenant the identifier names, with `ITenantStore<TKey>.FindByIdentifierAsync`. By default
   an identifier is the tenant's id: it is parsed as `TKey` (with the invariant culture) and looked up with
   `GetTenantAsync`. A store can map other names to its tenants, such as a slug or a custom domain for `Guid`-keyed
   tenants ([Identifiers other than the id](#identifiers-other-than-the-id)).

Resolution is part of `Tenantry.AspNetCore`. Console and worker apps have no request, and make a tenant current with
`ITenantScopeFactory` ([Non-HTTP hosts](non-http-hosts.md)).

## Built-in resolvers

| Method | Reads | For |
|--------|-------|-----|
| [`ResolveFromHeader(name)`](#header) | request header `name`, such as `X-Tenant-Id` | APIs and service-to-service calls |
| [`ResolveFromSubdomain(options)`](#subdomain) | the subdomain of the host | tenants on subdomains of your domain, such as `acme.example.com` |
| [`ResolveFromHost()`](#host-custom-domains) | the host name | tenants with domains of their own |
| [`ResolveFromRouteValue(key = "tenant")`](#route-value) | route value `key` | routes like `/api/{tenant}/…` |
| [`ResolveFromClaim(type = "tenant_id")`](#claim) | a claim on `HttpContext.User` | the tenant in the caller's token |
| [`ResolveFromQueryString(name = "tenantId")`](#query-string) | a query string parameter | development and tests only |
| `ResolveFromPropagationHeader(isTrustedCaller)` | the `tenantry-tenant-id` header another service sent | a caller `isTrustedCaller` accepts, after authentication; read as a tenant id ([Calling other services](http-propagation.md)) |

### Header

```csharp
tenant.ResolveFromHeader("X-Tenant-Id");
```

A header sent more than once names no tenant, so a proxy that sets the header must replace the client's, not add a
second one. The value is trimmed, and an empty one names no tenant
([`HeaderTenantResolver`](api/tenantry-aspnetcore-headertenantresolver.md)).

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
`acme.other.org` resolve nothing. `BaseDomains` starts empty. Add `localhost` for development, so `acme.localhost`
resolves to `acme` (browsers send `*.localhost` to the local machine).

Without base domains (`tenant.ResolveFromSubdomain()`), the first label of a host with at least three labels is the
tenant: `acme.app.example.com` and `app.example.com` resolve to `acme` and `app`. Shorter hosts, such as `example.com`,
`localhost` and `acme.localhost`, resolve nothing.

Either way, a subdomain in `IgnoredSubdomains` (`www` by default) and a host that is an IP address (a load balancer's
or Kubernetes' health probe) resolve nothing. The subdomain is returned in lower case
([`SubdomainTenantResolver`](api/tenantry-aspnetcore-subdomaintenantresolver.md)).

### Host (custom domains)

```csharp
tenant.ResolveFromSubdomain(options => options.BaseDomains.Add("example.com"));     // acme.example.com → "acme"
tenant.ResolveFromHost(options => options.ExcludedDomains.Add("example.com"));      // app.acme.com → "app.acme.com"
```

Add `ResolveFromHost()` last, and exclude your own domain. It returns the request's host name, for tenants that bring
their own domain, and your store's `FindByIdentifierAsync` maps it to a tenant. Every host resolves except an IP
address and one that is, or is under, one of `ExcludedDomains` (`localhost` by default). So a resolver added after it
never runs for those hosts. Without the exclusion, your own hosts that are not tenants (`www.example.com`,
`example.com`, `api.example.com`) each ask the store for a tenant on every request. A lookup that finds none is not
[cached](tenant-stores.md#caching). The host name is returned in lower case and without a port
([`HostTenantResolver`](api/tenantry-aspnetcore-hosttenantresolver.md)).

### Route value

```csharp
tenant.ResolveFromRouteValue();          // default key "tenant": /api/{tenant}/orders
tenant.ResolveFromRouteValue("org");     // /api/{org}/orders
```

Routing must run before `UseTenantry()`, which `WebApplication` arranges.

### Claim

```csharp
tenant.ResolveFromClaim();               // default claim type "tenant_id"
tenant.ResolveFromClaim("org_id");
```

`UseTenantry()` must come after `UseAuthentication()`, as the resolver reads the claim from `HttpContext.User`. The
caller cannot name a tenant its token does not carry. A user whose claims of the type list more than one tenant, as
repeated claims or a JSON array, resolves no tenant here, and the next resolver runs.

### Query string

```csharp
tenant.ResolveFromQueryString();         // ?tenantId=acme
tenant.ResolveFromQueryString("tenant"); // ?tenant=acme
```

> Do not use this resolver in production: query strings are logged, cached by CDNs and stored in browser history.
> It is for local development and tests.

## Resolver ordering and fallback

Put the most trusted source first. With several resolvers, the middleware uses the first identifier one returns, in
registration order, and `null`, an empty string or whitespace counts as none:

```csharp
tenant.ResolveFromClaim("tenant_id");             // 1. the tenant in the caller's token
tenant.ResolveFromHeader("X-Tenant-Id");          // 2. otherwise, the header
tenant.ValidateTenantAccessByClaim("tenant_id");  // the caller must be entitled to it
```

A header fallback lets any caller the claim does not resolve, anonymous ones too, name a tenant. Pair it with an
access validator such as `ValidateTenantAccessByClaim`.

If no resolver returns an identifier, the request proceeds without a tenant unless one is required
([Access control](access-control.md)). So does a request whose identifier names no tenant
([The middleware](aspnetcore-integration.md#the-middleware)). `app.UseTenantry()` throws at startup without a resolver
([Startup validation](aspnetcore-integration.md#startup-validation)).

## Custom resolvers

Implement `ITenantResolver` for another source, such as a cookie or a gRPC metadata entry. Return the identifier as
the request carries it, or `null` (or an empty string) when it carries none. Return only the identifier, not the
tenant: the store looks it up.

```csharp no-compile
public interface ITenantResolver
{
    ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken ct = default);
}
```

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

Register it by type, instance or factory. It runs in the order it was added among the built-in resolvers.

```csharp
tenant.UseResolver(new CookieTenantResolver());                      // a specific instance
tenant.UseResolver(sp => new CookieTenantResolver(/* deps */));      // by a factory, in each request's scope
tenant.UseResolver<CookieTenantResolver>();                          // created in each request's scope
```

- `UseResolver<TResolver>()` and the factory overload create the resolver in each request's scope, so it can depend
  on scoped services such as a `DbContext`.
- The request's scope disposes a resolver the factory returns, so return a new one. An instance is used for every
  request.
- `UseResolver<TResolver>()` returns the builder without its key type ([Registration](core-concepts.md#registration)).
- With `app.UseTenantResolution()`, add a resolver that reads `HttpContext.User` last, or use `ResolveFromClaim`
  instead ([Details](#with-usetenantresolution)).

An identifier the store does not know is rejected on an endpoint that requires a tenant: `404`, or `403` when access
validators are configured ([Status codes](aspnetcore-integration.md#status-codes)).

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

Without it, the default parses the identifier as the key type and looks it up by id. A store keyed by the identifier
itself (a `string` slug as the id) needs nothing more. To serve every request without a database
round trip, cache the lookups with [`CacheTenants`](tenant-stores.md#caching).

## Details

### With `UseTenantResolution()`

`app.UseTenantResolution()` first runs the resolvers before authentication, up to the first `ResolveFromClaim` or
`ResolveFromPropagationHeader`. A custom resolver that reads `HttpContext.User` finds nothing there, and a later
resolver's identifier is used. `app.UseTenantry()` runs them all again after authentication only when none returned
one ([How the two steps work](authentication-per-tenant.md#how-the-two-steps-work)).

### International domain names

Both host resolvers compare and return an international domain name in its ASCII form (`xn--…`), as DNS and
certificates have it. ASP.NET Core decodes it to Unicode, and the resolvers encode it again.
