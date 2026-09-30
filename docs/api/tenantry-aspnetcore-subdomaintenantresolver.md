# `SubdomainTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from the subdomain of the request host. For example, `acme.app.example.com` resolves to `acme`.

Without [`SubdomainTenantResolverOptions.BaseDomain`](tenantry-aspnetcore-subdomaintenantresolveroptions.md), the first label of a host with at least three labels is the tenant: `acme.example.com` resolves to `acme`, and `example.com`, `localhost` and `acme.localhost` resolve nothing. With it, only a host of exactly one label followed by the base domain resolves: with `example.com`, `acme.example.com` resolves to `acme`, while `example.com`, `other.org` and `x.acme.example.com` resolve nothing.

A subdomain in [`SubdomainTenantResolverOptions.IgnoredSubdomains`](tenantry-aspnetcore-subdomaintenantresolveroptions.md) (`www` by default) and a host that is an IP address resolve nothing.

```csharp
public sealed class SubdomainTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `SubdomainTenantResolver()`

Creates a resolver with the default options.

```csharp
public SubdomainTenantResolver()
```

### `SubdomainTenantResolver(SubdomainTenantResolverOptions)`

Creates a resolver with the given options, copied when it is created.

```csharp
public SubdomainTenantResolver(SubdomainTenantResolverOptions options)
```

Parameters:

- `options` [`SubdomainTenantResolverOptions`](tenantry-aspnetcore-subdomaintenantresolveroptions.md): The base domain and the subdomains to ignore.

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
