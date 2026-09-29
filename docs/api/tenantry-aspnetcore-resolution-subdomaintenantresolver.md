# `SubdomainTenantResolver` class

Namespace: `Tenantry.AspNetCore.Resolution` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from the first subdomain segment of the request host. For example, `acme.app.example.com` resolves to `acme`.

Requires the host to have at least three dot-separated segments to distinguish a true subdomain (e.g. `acme.app.com`) from a plain domain (e.g. `app.com`). Hosts with fewer than three segments (including `localhost` and `acme.localhost`) return `null`. For local development, use header-based resolution instead: `builder.ResolveFromHeader("X-Tenant-Id")`.

```csharp
public sealed class SubdomainTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-resolution-itenantresolver.md).

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
