using Microsoft.Extensions.Caching.Distributed;

namespace Tenantry.Caching;

/// <summary>
/// The registered <see cref="IDistributedCache"/>, with every key under the current tenant's prefix, for code that uses
/// <see cref="IDistributedCache"/> directly and keeps data per tenant. Without a tenant, every call throws
/// <see cref="TenantNotResolvedException"/>.
/// </summary>
/// <remarks>
/// <c>IsolateCaches()</c> registers it, and leaves <see cref="IDistributedCache"/> itself as it is: framework components
/// use it outside any tenant (session state, and <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/>'s own
/// second level), so isolating it everywhere would break them. A distributed cache cannot remove entries by tag, so
/// invalidating a tenant leaves these entries to expire.
/// </remarks>
public interface ITenantDistributedCache : IDistributedCache;
