# `HeaderTenantResolver` class

Namespace: `Tenantry.AspNetCore.Resolution` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a request header (e.g. `X-Tenant-Id`).

```csharp
public sealed class HeaderTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md).

## Constructors

### `HeaderTenantResolver(string)`

Resolves the tenant from a request header (e.g. `X-Tenant-Id`).

```csharp
public HeaderTenantResolver(string headerName)
```

Parameters:

- `headerName` `string`: The name of the header that carries the tenant identifier.

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
