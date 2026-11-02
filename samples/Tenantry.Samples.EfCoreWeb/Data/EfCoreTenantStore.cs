using Microsoft.EntityFrameworkCore;

namespace Tenantry.Samples.EfCoreWeb.Data;

/// <summary>
/// EF Core-backed tenant store. Registered as scoped: Tenantry resolves it in a scope
/// of its own for each lookup, with an AppDbContext of its own. It caches nothing:
/// Program.cs adds CacheTenants, so a request finds its tenant in memory.
/// Returns every tenant, active or not: Program.cs refuses inactive tenants with
/// ValidateTenantActivity, so tools that maintain every tenant's database still find them.
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
