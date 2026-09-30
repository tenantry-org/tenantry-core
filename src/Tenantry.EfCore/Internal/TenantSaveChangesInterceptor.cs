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
///   <item>Checks, once per model, that every tenant-owned entity type still has the tenant filter and concurrency token (<see cref="TenantModelCheck"/>).</item>
///   <item>Applies the configured <see cref="EfCoreIsolationOptions.OnMissingTenant"/> policy (default <c>Reject</c>) when tenant-owned entities are written without a resolved tenant.</item>
///   <item>Stamps <see cref="ITenantEntity{TKey}.TenantId"/> on all <c>Added</c> tenant-owned entities, and rejects one that already names another tenant.</item>
///   <item>Validates that every <c>Modified</c> or <c>Deleted</c> entity was loaded or attached as, and still belongs to, the current tenant.</item>
///   <item>Relies on the <c>TenantId</c> concurrency token so that a forged <c>TenantId</c> matches no row; EF Core then throws <see cref="DbUpdateConcurrencyException"/>, which is logged.</item>
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
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantIsolation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult ThrowingConcurrencyException(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result)
    {
        LogWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyException(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        LogWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    private static void ApplyTenantIsolation(DbContext? context)
    {
        if (context is not null)
        {
            TenantModelCheck.Verify(context)?.SavingChanges(context);
        }
    }

    // The model passed its check before the save that failed, so this finds its isolation without throwing.
    private static void LogWriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            TenantModelCheck.Verify(context)?.WriteMatchedNoRow(eventData);
        }
    }
}
