# `ClaimTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from a claim on the current request principal. A principal with more than one claim of the type resolves no tenant, so the next resolver runs.

```csharp
public sealed class ClaimTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `ClaimTenantResolver(string)`

Resolves the tenant from a claim on the current request principal. A principal with more than one claim of the type resolves no tenant, so the next resolver runs.

```csharp
public ClaimTenantResolver(string claimType = "tenant_id")
```

Parameters:

- `claimType` `string`: The type of the claim that carries the tenant identifier.

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
