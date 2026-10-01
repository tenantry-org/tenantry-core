# `HostTenantResolverOptions` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Options for [`HostTenantResolver`](tenantry-aspnetcore-hosttenantresolver.md), set with `tenant.ResolveFromHost(o => …)`.

```csharp
public sealed class HostTenantResolverOptions
```

## Properties

### `ExcludedDomains`

Domains whose hosts are not tenants: a host that is one of them, or a subdomain of one, resolves nothing. Contains `localhost` by default. Add your own domain, such as `example.com`, whose subdomains `ResolveFromSubdomain` resolves.

```csharp
public ISet<string> ExcludedDomains { get; }
```

Value: `ISet<string>`
