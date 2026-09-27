using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Records, for each pooled context that <see cref="TenantDatabaseDbContextFactory{TContext,TKey}"/> connects,
/// which lease and which tenant its connection string was set for.
/// </summary>
/// <remarks>
/// EF Core keeps a pooled context's connection string when the context returns to the pool, so a lease that
/// skipped the factory would silently use the previous tenant's database. <see cref="TenantDatabaseConnectionGuard{TKey}"/>
/// checks this record before every connection opens.
/// </remarks>
internal static class TenantDatabaseLeases
{
    private static readonly ConditionalWeakTable<DbContext, Lease> Leases = new();

    public static void Record(DbContext context, int lease, object? tenantId) =>
        Leases.AddOrUpdate(context, new Lease(lease, tenantId));

    public static bool TryGet(DbContext context, out Lease lease) => Leases.TryGetValue(context, out lease!);

    internal sealed record Lease(int Number, object? TenantId);
}

/// <summary>
/// Fails closed when a pooled database-per-tenant context would open a connection that was not set for its
/// current lease, or that belongs to a tenant other than the current one.
/// </summary>
internal sealed class TenantDatabaseConnectionGuard<TKey>(ITenantContext<TKey> tenantContext) : DbConnectionInterceptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var contextType = context.GetType().Name;

        if (!TenantDatabaseLeases.TryGet(context, out var lease) || lease.Number != context.ContextId.Lease)
        {
            throw new TenantIsolationViolationException(
                contextType,
                $"This pooled '{contextType}' was not connected to a tenant's database for its current lease, so it " +
                "would reuse the database of whichever tenant used it before. Obtain it from DI or from " +
                "IDbContextFactory, which AddTenantDbContextPool connects to the current tenant's database.");
        }

        if (!tenantContext.HasTenant || !Equals(tenantContext.CurrentTenantId, lease.TenantId))
        {
            throw new TenantIsolationViolationException(
                contextType,
                $"This '{contextType}' is connected to tenant '{lease.TenantId}''s database, but the current tenant is " +
                $"'{(tenantContext.HasTenant ? tenantContext.CurrentTenantId : "(none)")}'. Use a context created " +
                "while the tenant you are working as is current.");
        }
    }
}
