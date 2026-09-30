using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core interceptor that enforces tenant isolation on every <c>SaveChanges</c> call.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantEntity{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// It works with <em>any</em> <see cref="DbContext"/> — no base class required.
///
/// On every <c>SaveChanges</c> or <c>SaveChangesAsync</c>:
/// <list type="bullet">
///   <item>Applies the configured <see cref="EfCoreIsolationOptions.OnMissingTenant"/> policy (default <c>Reject</c>) when tenant-scoped entities are written without a resolved tenant.</item>
///   <item>Stamps <see cref="ITenantEntity{TKey}.TenantId"/> on all <c>Added</c> entities that implement <see cref="ITenantEntity{TKey}"/>, and rejects one that already names another tenant.</item>
///   <item>Validates that every <c>Modified</c> or <c>Deleted</c> entity was loaded or attached as, and still belongs to, the current tenant.</item>
///   <item>Relies on the <c>TenantId</c> concurrency token added by <c>ApplyTenantFilters</c> so that a forged <c>TenantId</c> matches no row; EF Core then throws <see cref="DbUpdateConcurrencyException"/>.</item>
///   <item>Checks, once per model, that every tenant-scoped entity type still has the tenant filter and concurrency token (<see cref="TenantModelCheck{TKey}"/>).</item>
///   <item>Throws <see cref="TenantIsolationViolationException"/> (before any data is written) if a cross-tenant violation is detected.</item>
/// </list>
///
/// Register via <c>builder.AddEfCoreIsolation()</c> inside <c>AddTenantry</c>, then call
/// <c>options.AddTenantInterceptors(sp)</c> in your <c>AddDbContext</c> callback.
/// </remarks>
internal sealed class TenantSaveChangesInterceptor<TKey>(
    ITenantContext<TKey> tenantContext,
    EfCoreIsolationOptions options,
    ILogger<TenantSaveChangesInterceptor<TKey>> logger)
    : SaveChangesInterceptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
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
        LogTenantEntityWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyException(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        LogTenantEntityWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    // A tenant-scoped UPDATE or DELETE that affects no row is either an ordinary concurrency conflict or
    // an attempt to write another tenant's row with a forged TenantId. The two cannot be told apart
    // without another query, so EF Core's DbUpdateConcurrencyException is left as is and logged here.
    private void LogTenantEntityWriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        foreach (var entry in eventData.Entries)
        {
            if (entry.Entity is ITenantEntity<TKey>)
            {
                logger.LogWarning(
                    "A {State} of tenant-scoped entity '{EntityType}' in tenant '{TenantId}' matched no row. " +
                    "The row does not exist, belongs to another tenant, or was changed concurrently",
                    entry.State,
                    entry.Entity.GetType().Name,
                    tenantContext.CurrentTenantId);
            }
        }
    }

    private void ApplyTenantIsolation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        TenantModelCheck<TKey>.Verify(context);

        if (!tenantContext.HasTenant)
        {
            HandleMissingTenant(context);
            return;
        }

        TenantWriteIsolationApplier.Apply(context.ChangeTracker.Entries(), tenantContext, violation =>
        {
            logger.LogError(
                "Tenant isolation violation: entity '{EntityType}' belongs to tenant '{OffendingTenantId}' " +
                "but the current tenant is '{ExpectedTenantId}'. Aborting SaveChanges",
                violation.TypeName,
                violation.OffendingTenantId,
                violation.ExpectedTenantId);
        });
    }

    private void HandleMissingTenant(DbContext context)
    {
        var scopedWrites = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is ITenantEntity<TKey> &&
                            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        // A save that writes no tenant-scoped entity (e.g. a host-level catalogue) needs no tenant.
        if (scopedWrites.Count == 0)
        {
            return;
        }

        var entityTypes = string.Join(", ", scopedWrites.Select(entry => entry.Metadata.ClrType.Name).Distinct());

        switch (options.OnMissingTenant)
        {
            case MissingTenantBehavior.Warn:
                logger.LogWarning(
                    "SaveChanges is writing tenant-scoped entities ({EntityTypes}) without a resolved tenant. " +
                    "Updates and deletes are not tenant-checked (EfCoreIsolationOptions.OnMissingTenant = Warn)",
                    entityTypes);
                break;

            case MissingTenantBehavior.Allow:
                break;

            default:
                throw new TenantNotResolvedException(
                    $"SaveChanges is writing tenant-scoped entities ({entityTypes}) without a resolved tenant. " +
                    "Run the write while a tenant is current (app.UseTenantry() for requests, " +
                    "ITenantScopeFactory.RunInScopeAsync or CreateScope elsewhere), or set " +
                    "EfCoreIsolationOptions.OnMissingTenant to Allow or Warn for maintenance code that deliberately " +
                    "writes across tenants.");
        }

        // Even when unscoped writes are allowed, a new row must name its tenant: an unowned row is never
        // visible through the tenant filter and belongs to no one.
        var unowned = scopedWrites.FirstOrDefault(entry =>
            entry.State == EntityState.Added &&
            TenantOwnership.IsUnstamped(((ITenantEntity<TKey>)entry.Entity).TenantId));

        if (unowned is not null)
        {
            throw new TenantNotResolvedException(
                $"A new '{unowned.Metadata.ClrType.Name}' is being saved without a resolved tenant and without a " +
                "TenantId. Set TenantId explicitly or save it while its tenant is current.");
        }
    }
}
