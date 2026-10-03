# `ITenantDistributedCache` interface

Namespace: `Tenantry.Caching` · Package: `Tenantry.Caching` · [API reference](README.md)

The registered `IDistributedCache`, with every key under the current tenant's prefix, for code that uses `IDistributedCache` directly and keeps data per tenant. Without a tenant, every call throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md).

`IsolateCaches()` registers it, and leaves `IDistributedCache` itself as it is: framework components use it outside any tenant (session state, and `HybridCache`'s own second level), so isolating it everywhere would break them. A distributed cache cannot remove entries by tag, so invalidating a tenant leaves these entries to expire.

```csharp
public interface ITenantDistributedCache : IDistributedCache
```
