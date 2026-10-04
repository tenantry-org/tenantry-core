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
///   <item>Relies on the <c>TenantId</c> concurrency token so that a forged <c>TenantId</c> matches no row; EF Core then throws <see cref="DbUpdateConcurrencyException"/>, which <see cref="TenantSaveNoticeInterceptor"/> logs and, when other statements of the save rely on that check (<see cref="AtomicSave"/>), throws ahead of every application interceptor.</item>
///   <item>Throws <see cref="TenantIsolationViolationException"/> (before any data is written) if a cross-tenant write is detected.</item>
/// </list>
/// It is added with the context's other interceptors where <c>UseTenantry()</c> is called, so it runs after those
/// registered before it and checks the entities they add. The save's other notices go to
/// <see cref="TenantSaveNoticeInterceptor"/>, which runs before every interceptor the application adds. It holds no
/// state (the tenant comes from each context's application service provider), so one instance serves every context.
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

    private static void ApplyTenantIsolation(DbContext? context)
    {
        if (context is not null)
        {
            TenantContextGuard.CheckAll(context);
            TenantModelCheck.Verify(context)?.SavingChanges(context);
        }
    }
}
