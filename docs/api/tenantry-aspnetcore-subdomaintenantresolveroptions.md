# `SubdomainTenantResolverOptions` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Options for [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md), set with `tenant.ResolveFromSubdomain(o => …)`.

```csharp
public sealed class SubdomainTenantResolverOptions
```

## Properties

### `BaseDomains`

The domains whose subdomains are tenants, such as `example.com` for `acme.example.com`, and `localhost` for `acme.localhost` in development. When set, only a host of exactly one label followed by one of them resolves a tenant. When empty (the default), the first label of any host with at least three labels is the tenant.

```csharp
public ISet<string> BaseDomains { get; }
```

Value: `ISet<string>`

### `IgnoredSubdomains`

Subdomains that are never tenants, compared without regard to case. Contains `www` by default; add others, such as `api` or `app`, that serve the application itself.

```csharp
public ISet<string> IgnoredSubdomains { get; }
```

Value: `ISet<string>`
