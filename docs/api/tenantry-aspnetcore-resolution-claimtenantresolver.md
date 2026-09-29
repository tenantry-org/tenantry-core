# `ClaimTenantResolver` class

Namespace: `Tenantry.AspNetCore.Resolution` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a claim on the current request principal.

```csharp
public sealed class ClaimTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md).

## Constructors

### `ClaimTenantResolver(string)`

Resolves the tenant from a claim on the current request principal.

```csharp
public ClaimTenantResolver(string claimType = "tenant_id")
```

Parameters:

- `claimType` `string`: The type of the claim that carries the tenant identifier.

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
