using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Records, for each context that <see cref="TenantDatabaseContexts{TContext,TKey}"/> connects, which lease (always
/// 0 for a context that is not pooled) and which tenant its connection string was set for.
/// </summary>
/// <remarks>
/// EF Core keeps a pooled context's connection string when the context returns to the pool, so a lease that
/// skipped the factory would silently use the previous tenant's database, and any context could be kept and used
/// after the tenant changes. <see cref="TenantDatabaseGuard{TKey}"/> checks this record before a connection opens and
/// before every command.
/// </remarks>
internal static class TenantDatabaseLeases
{
    private static readonly ConditionalWeakTable<DbContext, Lease> Leases = [];

    // EF Core runs some commands without a context (a HiLo sequence fetch, for example), so the guard maps
    // the command's connection back to the context that owns it. A pooled context keeps its DbConnection
    // across leases (the pool closes it but does not dispose it).
    private static readonly ConditionalWeakTable<DbConnection, DbContext> Connections = [];

    /// <param name="context">The context.</param>
    /// <param name="lease">Its lease.</param>
    /// <param name="tenantId">The tenant it was connected for.</param>
    /// <param name="pendingTenant">
    /// The tenant whose connection string is still to be read, when the context opens a connection; otherwise null.
    /// </param>
    public static void Record(DbContext context, int lease, object? tenantId, object? pendingTenant = null)
    {
        var connection = context.Database.GetDbConnection();
        Leases.AddOrUpdate(context, new Lease(lease, tenantId, connection, context.Database.GetConnectionString(), pendingTenant));
        Connections.AddOrUpdate(connection, context);
    }

    /// <summary>Records the connection string read for a lease that was pending.</summary>
    public static void Connected(DbContext context, Lease lease) =>
        Leases.AddOrUpdate(context, lease with { ConnectionString = context.Database.GetConnectionString(), PendingTenant = null });

    public static bool TryGet(DbContext context, out Lease lease) => Leases.TryGetValue(context, out lease!);

    public static DbContext? FindContext(DbConnection? connection) =>
        connection is not null && Connections.TryGetValue(connection, out var context) ? context : null;

    internal sealed record Lease(
        int Number,
        object? TenantId,
        DbConnection Connection,
        string? ConnectionString,
        object? PendingTenant);
}

/// <summary>
/// Fails closed when a database-per-tenant context would use a connection that was not set for it (and, pooled, for
/// its current lease), or that belongs to a tenant other than the current one.
/// </summary>
/// <remarks>
/// <see cref="TenantContextGuard"/> runs the check. It also fails if the application replaced the lease's connection
/// or connection string, and it reads a lease's connection string when the provider can only read asynchronously.
/// </remarks>
internal sealed class TenantDatabaseGuard<TKey>(
    ITenantContext<TKey> tenantContext,
    ITenantConnectionStringProvider<TKey> connectionStrings)
    : TenantContextGuard
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        var context = eventData.Context ?? FindContext(connection);
        CheckContext(context);

        if (context is not null && TenantDatabaseLeases.TryGet(context, out var lease) && lease.PendingTenant is not null)
        {
            throw new InvalidOperationException(
                $"This '{context.GetType().Name}' reads its tenant's connection string asynchronously (only " +
                "GetConnectionStringAsync is configured), so it cannot open a connection synchronously. Use the " +
                "asynchronous EF Core methods (ToListAsync, SaveChangesAsync), or also set GetConnectionString.");
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context ?? FindContext(connection);
        CheckContext(context);

        if (context is not null && TenantDatabaseLeases.TryGet(context, out var lease) && lease.PendingTenant is ITenantDescriptor<TKey> tenant)
        {
            context.Database.SetConnectionString(await connectionStrings.GetAsync(tenant, cancellationToken));
            TenantDatabaseLeases.Connected(context, lease);
        }

        return result;
    }

    // A pooled context keeps its DbConnection across leases, so the record made when it was connected names it.
    protected override DbContext? FindContext(DbConnection? connection) => TenantDatabaseLeases.FindContext(connection);

    protected override void Check(DbContext context) => CheckContext(context);

    private void CheckContext(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var contextType = context.GetType().Name;
        var currentTenantId = tenantContext.HasTenant ? tenantContext.CurrentTenantId?.ToString() : null;

        if (!TenantDatabaseLeases.TryGet(context, out var lease) || lease.Number != context.ContextId.Lease)
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.TenantDatabaseMismatch,
                contextType,
                $"This '{contextType}' was not connected to a tenant's database for its current lease, so it would " +
                "reuse the database of whichever tenant used it before. Obtain it from DI or from IDbContextFactory, " +
                "which AddDbContextPerTenantDatabase connects to the current tenant's database.",
                expectedTenantId: currentTenantId);
        }

        var leaseTenantId = lease.TenantId?.ToString();

        if (!ReferenceEquals(context.Database.GetDbConnection(), lease.Connection)
            || !string.Equals(context.Database.GetConnectionString(), lease.ConnectionString, StringComparison.Ordinal))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.TenantDatabaseMismatch,
                contextType,
                $"This '{contextType}''s connection was changed after it was connected to tenant '{lease.TenantId}''s " +
                "database. Do not call SetConnectionString or SetDbConnection on a context from AddDbContextPerTenantDatabase.",
                leaseTenantId,
                currentTenantId);
        }

        if (!tenantContext.HasTenant || !Equals(tenantContext.CurrentTenantId, lease.TenantId))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.TenantDatabaseMismatch,
                contextType,
                $"This '{contextType}' is connected to tenant '{lease.TenantId}''s database, but the current tenant is " +
                $"'{currentTenantId ?? "(none)"}'. Use a context created while the tenant you are working as is current.",
                leaseTenantId,
                currentTenantId);
        }
    }
}
