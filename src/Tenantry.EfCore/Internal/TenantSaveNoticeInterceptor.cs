using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The EF Core interceptor that tells <see cref="AtomicSave"/> how each save of a context that uses <c>UseTenantry()</c>
/// ended, and throws a failed tenant check that other statements of the save rely on.
/// </summary>
/// <remarks>
/// <c>UseTenantry()</c> registers it as an <see cref="IInterceptor"/> service of EF Core's internal service provider,
/// at the start of the list (<see cref="TenantryOptionsExtension"/>). EF Core runs such interceptors before every
/// interceptor added with <c>AddInterceptors</c>, whatever the order the application configured them in, and stops at
/// the first that throws, so an application interceptor that throws from one of these notices (as one that translates
/// save failures does) can no longer keep it from Tenantry. It holds no state, so one instance serves every context.
/// </remarks>
internal sealed class TenantSaveNoticeInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveNoticeInterceptor Instance = new();

    private TenantSaveNoticeInterceptor()
    {
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

    // The model passed its check before the save that failed, so this finds its isolation without throwing. Any
    // concurrency failure of a save that sent statements is a failure of the transaction it ran in, as an application
    // interceptor after this one may suppress or replace it and a SaveChangesFailed handler subscribed before
    // Tenantry's may keep its only other notice from Tenantry. A failed check that other statements of the save rely on
    // is thrown here, ahead of every application interceptor: one that suppressed it would have EF Core commit the rest.
    private static void WriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        TenantModelCheck.Verify(context)?.WriteMatchedNoRow(eventData);
        AtomicSave.ConcurrencyFailed(context);

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
