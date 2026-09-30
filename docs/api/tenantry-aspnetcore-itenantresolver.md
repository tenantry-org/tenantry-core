# `ITenantResolver` interface

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Extracts a tenant identifier from an HTTP request. Multiple resolvers can be registered; the middleware tries them in priority order and uses the first non-null result.

```csharp
public interface ITenantResolver
```

Derived types: [`ClaimTenantResolver`](tenantry-aspnetcore-claimtenantresolver.md), [`HeaderTenantResolver`](tenantry-aspnetcore-headertenantresolver.md), [`QueryStringTenantResolver`](tenantry-aspnetcore-querystringtenantresolver.md), [`RouteValueTenantResolver`](tenantry-aspnetcore-routevaluetenantresolver.md), [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md).

## Methods

### `ResolveAsync(HttpContext, CancellationToken)`

Attempts to extract a tenant ID from the current request.

```csharp
ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
```

Parameters:

- `context` `HttpContext`: The current HTTP context.
- `cancellationToken` `CancellationToken`: Cancellation token.

Returns: `ValueTask<string>`: The resolved tenant ID string, or `null` if this resolver cannot determine the tenant from the current request.
