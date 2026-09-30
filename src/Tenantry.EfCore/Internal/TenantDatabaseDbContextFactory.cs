using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Leases contexts from EF Core's pool and connects each lease to the current tenant's database.
/// </summary>
internal sealed class TenantDatabaseDbContextFactory<
    [DynamicallyAccessedMembers(TenantryDbContextPoolServiceCollectionExtensions.ContextMembers)] TContext, TKey>(
    IDbContextFactory<TContext> pool,
    ITenantConnectionStringProvider<TKey> connectionStrings,
    ITenantContext<TKey> tenantContext)
    : IDbContextFactory<TContext>
    where TContext : DbContext
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public TContext CreateDbContext()
    {
        // The connection string first: without a tenant this throws before a context is leased.
        var tenant = CurrentTenant();
        var connectionString = connectionStrings.Get(tenant);
        return Connect(pool.CreateDbContext(), tenant, connectionString);
    }

    public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var tenant = CurrentTenant();
        var connectionString = await connectionStrings.GetAsync(tenant, cancellationToken);
        return Connect(await pool.CreateDbContextAsync(cancellationToken), tenant, connectionString);
    }

    private ITenantDescriptor<TKey> CurrentTenant() =>
        tenantContext.CurrentTenant
        ?? throw new TenantNotResolvedException(
            $"No tenant is current, so there is no tenant database to connect this '{typeof(TContext).Name}' to. " +
            "Create it during a request (after app.UseTenantry()) or inside a scope from ITenantScopeFactory.");

    private static TContext Connect(TContext context, ITenantDescriptor<TKey> tenant, string connectionString)
    {
        try
        {
            context.Database.SetConnectionString(connectionString);
            TenantDatabaseLeases.Record(context, context.ContextId.Lease, tenant.TenantId);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}
