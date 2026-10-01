# `ITenantResolver` interface

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Reads a tenant identifier from an HTTP request: the tenant's id, or a name the tenant store maps to a tenant, such as a subdomain or a host name (see [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md)). Multiple resolvers can be registered; the middleware tries them in registration order and uses the first identifier one returns.

```csharp
public interface ITenantResolver
```

Derived types: [`ClaimTenantResolver`](tenantry-aspnetcore-claimtenantresolver.md), [`HeaderTenantResolver`](tenantry-aspnetcore-headertenantresolver.md), [`HostTenantResolver`](tenantry-aspnetcore-hosttenantresolver.md), [`QueryStringTenantResolver`](tenantry-aspnetcore-querystringtenantresolver.md), [`RouteValueTenantResolver`](tenantry-aspnetcore-routevaluetenantresolver.md), [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md).

## Methods

### `ResolveAsync(HttpContext, CancellationToken)`

Attempts to read a tenant identifier from the current request.

```csharp
ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
```

Parameters:

- `context` `HttpContext`: The current HTTP context.
- `cancellationToken` `CancellationToken`: Cancellation token.

Returns: `ValueTask<string>`: The identifier, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) (or an empty string) if this resolver cannot determine the tenant from the current request. Return it as the request carries it: the tenant store finds the tenant it names.
