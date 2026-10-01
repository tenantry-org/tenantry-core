# `TenantStoreCacheOptions` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

How Tenantry caches the tenants it reads from the tenant store. Set with `tenant.CacheTenants(o => …)`.

```csharp
public sealed class TenantStoreCacheOptions
```

## Properties

### `Duration`

How long a tenant read from the store is reused before the store is asked again. Defaults to 5 minutes. It must be positive.

```csharp
public TimeSpan Duration { get; set; }
```

Value: `TimeSpan`
