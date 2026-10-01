# `HostTenantResolver` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Resolves the tenant from the request's host name, for tenants that bring their own domain: a request to `app.acme.com` has the identifier `app.acme.com`, which your tenant store's [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md) maps to a tenant.

The host name is returned in lower case, without a port or a trailing dot, and an international domain name in its ASCII form (`xn--…`), as DNS has it. A host that is an IP address (a load balancer's or Kubernetes' health probe), or is or is under one of [`HostTenantResolverOptions.ExcludedDomains`](tenantry-aspnetcore-hosttenantresolveroptions.md) (`localhost` by default), resolves nothing.

Every other host resolves, so resolvers added after it never run for those hosts: add it last. Exclude your own domain, whose subdomains `ResolveFromSubdomain` resolves, so its hosts (`www`, the apex) do not ask the tenant store for a tenant on every request.

```csharp
public sealed class HostTenantResolver : ITenantResolver
```

Implements [`ITenantResolver`](tenantry-aspnetcore-itenantresolver.md).

## Constructors

### `HostTenantResolver()`

Creates a resolver with the default options.

```csharp
public HostTenantResolver()
```

### `HostTenantResolver(HostTenantResolverOptions)`

Creates a resolver with the given options, copied when it is created.

```csharp
public HostTenantResolver(HostTenantResolverOptions options)
```

Parameters:

- `options` [`HostTenantResolverOptions`](tenantry-aspnetcore-hosttenantresolveroptions.md): The domains whose hosts are not tenants.

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
