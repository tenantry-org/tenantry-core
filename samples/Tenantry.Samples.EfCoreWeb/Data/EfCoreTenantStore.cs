using Microsoft.EntityFrameworkCore;
using Tenantry.Core;

namespace Tenantry.Samples.EfCoreWeb.Data;

/// <summary>
/// EF Core-backed tenant store. Registered as scoped — one instance per request,
/// sharing the request's AppDbContext. No caching: the middleware calls this once
/// per request, so a single DB lookup is fine.
/// </summary>
public sealed class EfCoreTenantStore(AppDbContext db, ILogger<EfCoreTenantStore> logger)
    : ITenantStore<string>
{
    /// <inheritdoc />
    public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (tenant is null)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Tenant {TenantId} not found", tenantId);
            }
            
            return null;
        }

        if (tenant.IsActive)
        {
            return tenant;
        }
        
        logger.LogWarning("Tenant {TenantId} is inactive", tenantId);
        return null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive)
            .ToListAsync<ITenantDescriptor<string>>(cancellationToken);
    }
}
