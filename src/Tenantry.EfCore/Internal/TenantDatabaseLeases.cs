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
/// The check runs before EF Core opens a connection and again before every command. EF Core raises no
/// <c>ConnectionOpening</c> for a connection that is already open, whether the application opened it or a
/// transaction did, so a context opened under one tenant and then used under another is caught only when its
/// next command runs. <c>SaveChanges</c> is also checked before it starts, because EF Core wraps an exception
/// thrown while a save runs its commands in a <c>DbUpdateException</c>. A command EF Core runs without a
/// context (a HiLo sequence fetch) is attributed to the context that owns its connection. The check also fails
/// if the application replaced the lease's connection or connection string.
/// </remarks>
internal sealed class TenantDatabaseGuard<TKey>(
    ITenantContext<TKey> tenantContext,
    ITenantConnectionStringProvider<TKey> connectionStrings)
    : IDbConnectionInterceptor, IDbCommandInterceptor, ISaveChangesInterceptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        var context = eventData.Context ?? TenantDatabaseLeases.FindContext(connection);
        Check(context);

        if (context is not null && TenantDatabaseLeases.TryGet(context, out var lease) && lease.PendingTenant is not null)
        {
            throw new InvalidOperationException(
                $"This '{context.GetType().Name}' reads its tenant's connection string asynchronously (only " +
                "GetConnectionStringAsync is configured), so it cannot open a connection synchronously. Use the " +
                "asynchronous EF Core methods (ToListAsync, SaveChangesAsync), or also set GetConnectionString.");
        }

        return result;
    }

    public async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context ?? TenantDatabaseLeases.FindContext(connection);
        Check(context);

        if (context is not null && TenantDatabaseLeases.TryGet(context, out var lease) && lease.PendingTenant is ITenantDescriptor<TKey> tenant)
        {
            context.Database.SetConnectionString(await connectionStrings.GetAsync(tenant, cancellationToken));
            TenantDatabaseLeases.Connected(context, lease);
        }

        return result;
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return result;
    }

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context ?? TenantDatabaseLeases.FindContext(command.Connection));
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
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
