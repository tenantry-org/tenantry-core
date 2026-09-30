# `SubdomainTenantResolverOptions` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Options for [`SubdomainTenantResolver`](tenantry-aspnetcore-subdomaintenantresolver.md), set with `tenant.ResolveFromSubdomain(o => …)`.

```csharp
public sealed class SubdomainTenantResolverOptions
```

## Properties

### `BaseDomain`

The domain whose subdomains are tenants, such as `example.com` for `acme.example.com`, or `localhost` for `acme.localhost` in development. When set, only a host of exactly one label followed by this domain resolves a tenant. When not set, the first label of any host with at least three labels is the tenant.

```csharp
public string? BaseDomain { get; set; }
```

Value: `string`

### `IgnoredSubdomains`

Subdomains that are never tenants, compared without regard to case. Contains `www` by default; add others, such as `api` or `app`, that serve the application itself.

```csharp
public ISet<string> IgnoredSubdomains { get; }
```

Value: `ISet<string>`
