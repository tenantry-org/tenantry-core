# `QueryStringTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a query string parameter (e.g. `?tenantId=acme`).

Intended for local development and testing convenience only. Do not enable in production — query string parameters are logged and may appear in analytics, CDN caches, and browser history.

```csharp
public sealed class QueryStringTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `QueryStringTenantResolver(string)`

Resolves the tenant from a query string parameter (e.g. `?tenantId=acme`).

```csharp
public QueryStringTenantResolver(string parameterName = "tenantId")
```

Parameters:

- `parameterName` `string`: The name of the query string parameter that carries the tenant identifier.

Intended for local development and testing convenience only. Do not enable in production — query string parameters are logged and may appear in analytics, CDN caches, and browser history.

## Methods

### `ResolveAsync(HttpContext, CancellationToken)`

Attempts to read a tenant identifier from the current request.

```csharp
public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
```

Parameters:

- `context` `HttpContext`: The current HTTP context.
- `cancellationToken` `CancellationToken`: Cancellation token.

Returns: `ValueTask<string>`: The identifier, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) (or an empty string) if this resolver cannot determine the tenant from the current request. Return it as the request carries it: the tenant store finds the tenant it names.
