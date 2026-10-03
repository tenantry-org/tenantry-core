using System.ComponentModel;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tenantry.EfCore;

/// <summary>
/// An interceptor that checks a context before it opens a connection, before every command it runs and before
/// <c>SaveChanges</c>, so a context that would reach the wrong tenant's data throws instead. Derive from it to fail
/// closed on a condition of your own, such as a context whose schema is not the current tenant's.
/// </summary>
/// <remarks>
/// <para>
/// Add it to a context's options with <c>AddInterceptors</c>, or from an <see cref="ITenantDbContextOptionsContributor"/>.
/// EF Core raises no event for a connection that is already open, so a context opened under one tenant and then used
/// under another is caught at its next command. <c>SaveChanges</c> is checked before it starts, because EF Core wraps
/// an exception thrown while a save runs its commands in a <c>DbUpdateException</c>. A command EF Core runs without a
/// context (a HiLo sequence fetch) is checked against the context that opened its connection.
/// </para>
/// <para>
/// In a context that uses <c>UseTenantry()</c>, the check runs before Tenantry stamps the save's new entities with the
/// current tenant, wherever the guard is among the context's interceptors, so a save it refuses leaves the tracked
/// entities as they were. Interceptors of your own run in the order they were added: add the guard before one that
/// changes entities when a save starts.
/// </para>
/// <para>
/// Throw <see cref="TenantNotResolvedException"/> when the context needs a tenant and none is current, and
/// <see cref="TenantIsolationViolationException"/> when it would use another tenant's data.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class TenantContextGuard : IDbConnectionInterceptor, IDbCommandInterceptor, ISaveChangesInterceptor
{
    // The context that opened each connection, for commands EF Core runs without one.
    private static readonly ConditionalWeakTable<DbConnection, DbContext> Openers = [];

    /// <summary>Throws when <paramref name="context"/> must not touch the database now.</summary>
    /// <param name="context">The context about to open a connection, run a command or save.</param>
    protected abstract void Check(DbContext context);

    /// <inheritdoc />
    public virtual InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CheckContext(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CheckContext(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public virtual InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        CheckContext(Opening(connection, eventData));
        return result;
    }

    /// <inheritdoc />
    public virtual ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        CheckContext(Opening(connection, eventData));
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CheckContext(Running(command, eventData));
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CheckContext(Running(command, eventData));
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        CheckContext(Running(command, eventData));
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CheckContext(Running(command, eventData));
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        CheckContext(Running(command, eventData));
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        CheckContext(Running(command, eventData));
        return ValueTask.FromResult(result);
    }

    /// <summary>The context a connection event belongs to, recorded for the connection's later commands.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="eventData">The event.</param>
    /// <returns>The context, or <see langword="null"/> when none is known.</returns>
    protected DbContext? Opening(DbConnection connection, ConnectionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            Openers.AddOrUpdate(connection, context);
            return context;
        }

        return FindContext(connection);
    }

    /// <summary>
    /// The context that owns <paramref name="connection"/>, for an event that names none. By default, the last context
    /// that opened it.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The context, or <see langword="null"/> when none is known.</returns>
    protected virtual DbContext? FindContext(DbConnection? connection) =>
        connection is not null && Openers.TryGetValue(connection, out var context) ? context : null;

    /// <summary>
    /// Runs the check of every guard among <paramref name="context"/>'s interceptors, for Tenantry to call before it
    /// changes the save's entities.
    /// </summary>
    internal static void CheckAll(DbContext context)
    {
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors;

        if (interceptors is null)
        {
            return;
        }

        foreach (var interceptor in interceptors)
        {
            if (interceptor is TenantContextGuard guard)
            {
                guard.Check(context);
            }
        }
    }

    private DbContext? Running(DbCommand command, CommandEventData eventData) =>
        eventData.Context ?? FindContext(command.Connection);

    private void CheckContext(DbContext? context)
    {
        if (context is not null)
        {
            Check(context);
        }
    }
}
