using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Leases contexts from EF Core's pool and connects each lease to the current tenant's database.
/// </summary>
internal sealed class TenantDatabaseDbContextFactory<
    [DynamicallyAccessedMembers(TenantDbContextPoolExtensions.ContextMembers)] TContext, TKey>(
    IDbContextFactory<TContext> pool,
    ITenantConnectionStringResolver<TKey> connectionStrings,
    ITenantContext<TKey> tenantContext)
    : IDbContextFactory<TContext>
    where TContext : DbContext
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public TContext CreateDbContext()
    {
        // Resolve first: without a tenant this throws before a context is leased.
        var connectionString = connectionStrings.Resolve();
        return Connect(pool.CreateDbContext(), connectionString);
    }

    public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await connectionStrings.ResolveAsync(cancellationToken);
        return Connect(await pool.CreateDbContextAsync(cancellationToken), connectionString);
    }

    private TContext Connect(TContext context, string connectionString)
    {
        try
        {
            context.Database.SetConnectionString(connectionString);
            TenantDatabaseLeases.Record(context, context.ContextId.Lease, tenantContext.CurrentTenantId);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}
