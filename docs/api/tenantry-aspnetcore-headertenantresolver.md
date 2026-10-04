# `HeaderTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a request header (e.g. `X-Tenant-Id`).

A header sent more than once names no tenant. A proxy that sets the header must replace one the client sent, not add another.

```csharp
public sealed class HeaderTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `HeaderTenantResolver(string)`

Resolves the tenant from a request header (e.g. `X-Tenant-Id`).

```csharp
public HeaderTenantResolver(string headerName)
```

Parameters:

- `headerName` `string`: The name of the header that carries the tenant identifier.

A header sent more than once names no tenant. A proxy that sets the header must replace one the client sent, not add another.

## Methods

### `ResolveAsync(HttpContext, CancellationToken)`

Attempts to read a tenant identifier from the current request.

```csharp
public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
```

Parameters:

- `context` `HttpContext`: The current HTTP context.
- `cancellationToken` `CancellationToken`: Cancellation token.

Returns: `ValueTask<string>`: The identifier, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) (or an empty string or whitespace) if this resolver cannot determine the tenant from the current request. Return it as the request carries it: the tenant store finds the tenant it names.
