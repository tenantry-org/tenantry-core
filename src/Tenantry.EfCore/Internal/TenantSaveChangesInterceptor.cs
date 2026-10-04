using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core interceptor that enforces tenant isolation on every <c>SaveChanges</c> call of a context that uses
/// <c>UseTenantry()</c>.
/// </summary>
/// <remarks>
/// On every <c>SaveChanges</c> or <c>SaveChangesAsync</c>:
/// <list type="bullet">
///   <item>Runs the check of every <see cref="TenantContextGuard"/> among the context's interceptors first, wherever it was added, so a save a guard refuses leaves the tracked entities unstamped.</item>
///   <item>Checks, once per model, that every tenant-owned entity type still has the tenant filter and concurrency token (<see cref="TenantModelCheck"/>).</item>
///   <item>Runs <see cref="TenantWriteGuard{TKey}"/>: it applies the configured <see cref="EfCoreIsolationOptions.OnMissingTenant"/> policy (default <c>Reject</c>) when tenant-owned entities are written without a resolved tenant; stamps <see cref="ITenantEntity{TKey}.TenantId"/> on all <c>Added</c> tenant-owned entities, and rejects one that already names another tenant; validates that every <c>Modified</c> or <c>Deleted</c> entity was loaded or attached as, and still belongs to, the current tenant; and checks owned entities through their owner, reading the stored tenant of an owner whose <c>TenantId</c> is part of a key.</item>
///   <item>Relies on the <c>TenantId</c> concurrency token so that a forged <c>TenantId</c> matches no row; EF Core then throws <see cref="DbUpdateConcurrencyException"/>, which is logged. When other statements of the save rely on that check (<see cref="AtomicSave"/>), no interceptor can suppress it, and the save is kept all-or-nothing.</item>
///   <item>Throws <see cref="TenantIsolationViolationException"/> (before any data is written) if a cross-tenant write is detected.</item>
/// </list>
/// It holds no state (the tenant comes from each context's application service provider), so one instance serves
/// every context.
/// </remarks>
internal sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveChangesInterceptor Instance = new();

    private TenantSaveChangesInterceptor()
    {
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTenantIsolation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            TenantContextGuard.CheckAll(context);

            if (TenantModelCheck.Verify(context) is { } isolation)
            {
                await isolation.SavingChangesAsync(context, cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult ThrowingConcurrencyException(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result)
    {
        WriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyException(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        WriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context)
        {
            AtomicSave.Saved(context, eventData.EntitiesSavedCount);
        }

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            AtomicSave.Saved(context, eventData.EntitiesSavedCount);
        }

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        EndFailedSave(eventData.Context, eventData.Exception);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        EndFailedSave(eventData.Context, eventData.Exception);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        EndFailedSave(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        EndFailedSave(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private static void ApplyTenantIsolation(DbContext? context)
    {
        if (context is not null)
        {
            TenantContextGuard.CheckAll(context);
            TenantModelCheck.Verify(context)?.SavingChanges(context);
        }
    }

    // The model passed its check before the save that failed, so this finds its isolation without throwing. A failed
    // check that other statements of the save rely on is thrown here, whatever another interceptor makes of it: one
    // that suppressed it, before or after this one, would have EF Core commit the rest.
    private static void WriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        TenantModelCheck.Verify(context)?.WriteMatchedNoRow(eventData);

        if (AtomicSave.IsCheck(context, eventData.Entries))
        {
            throw eventData.Exception;
        }
    }

    // EF Core tells interceptors of a cancelled save, and of a failed one unless it is a concurrency failure, which
    // only the context's SaveChangesFailed event reports (AtomicSave listens to it).
    private static void EndFailedSave(DbContext? context, Exception? failure = null)
    {
        if (context is not null)
        {
            AtomicSave.Failed(context, failure);
        }
    }
}
