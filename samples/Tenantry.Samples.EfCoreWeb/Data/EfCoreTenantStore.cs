using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace Tenantry.Samples.EfCoreWeb.Data;

/// <summary>
/// EF Core-backed tenant store. Registered as scoped — one instance per request,
/// sharing the request's AppDbContext. No caching: the middleware calls this once
/// per request, so a single DB lookup is fine.
/// Returns every tenant, active or not: Program.cs refuses inactive tenants with an
/// access validator, so tools that maintain every tenant's database still find them.
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

        if (tenant is null && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Tenant {TenantId} not found", tenantId);
        }

        return tenant;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .ToListAsync<ITenantDescriptor<string>>(cancellationToken);
    }
}
