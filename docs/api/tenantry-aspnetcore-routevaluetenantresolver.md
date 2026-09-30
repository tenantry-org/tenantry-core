# `RouteValueTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a route value (e.g. `/api/{tenant}/resource`).

```csharp
public sealed class RouteValueTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `RouteValueTenantResolver(string)`

Resolves the tenant from a route value (e.g. `/api/{tenant}/resource`).

```csharp
public RouteValueTenantResolver(string routeValueKey = "tenant")
```

Parameters:

- `routeValueKey` `string`: The name of the route value that carries the tenant identifier.

## Methods

### `ResolveAsync(HttpContext, CancellationToken)`

Attempts to extract a tenant ID from the current request.

```csharp
public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
```

Parameters:

- `context` `HttpContext`: The current HTTP context.
- `cancellationToken` `CancellationToken`: Cancellation token.

Returns: `ValueTask<string>`: The resolved tenant ID string, or `null` if this resolver cannot determine the tenant from the current request.
