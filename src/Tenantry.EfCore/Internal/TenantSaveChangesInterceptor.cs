using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core interceptor that enforces tenant isolation on every <c>SaveChanges</c> call.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// It works with <em>any</em> <see cref="DbContext"/> — no base class required.
///
/// On every <c>SaveChanges</c> or <c>SaveChangesAsync</c>:
/// <list type="bullet">
///   <item>Applies the configured <see cref="EfCoreIsolationOptions.OnMissingTenant"/> policy when no tenant is resolved.</item>
///   <item>Stamps <see cref="ITenantScoped{TKey}.TenantId"/> on all <c>Added</c> entities that implement <see cref="ITenantScoped{TKey}"/>.</item>
///   <item>Validates that every <c>Modified</c> or <c>Deleted</c> entity was loaded or attached as, and still belongs to, the current tenant.</item>
///   <item>Relies on the <c>TenantId</c> concurrency token added by <c>ApplyTenantFilters</c> so that a forged <c>TenantId</c> matches no row; EF Core then throws <see cref="DbUpdateConcurrencyException"/>.</item>
///   <item>When <see cref="EfCoreIsolationOptions.DetectSpoofedWrites"/> is enabled, also rejects <c>Added</c> entities pre-stamped with a foreign tenant.</item>
///   <item>Throws <see cref="TenantIsolationViolationException"/> (before any data is written) if a cross-tenant violation is detected.</item>
/// </list>
///
/// Register via <c>builder.AddEfCoreIsolation()</c> inside <c>AddTenantry</c> or <c>AddTenantryCore</c>, then call
/// <c>options.AddTenantInterceptors(sp)</c> in your <c>AddDbContext</c> callback.
/// </remarks>
internal sealed class TenantSaveChangesInterceptor<TKey>(
    ITenantContext<TKey> tenantContext,
    EfCoreIsolationOptions options,
    StrictIsolationValidator<TKey> spoofValidator,
    ILogger<TenantSaveChangesInterceptor<TKey>> logger)
    : SaveChangesInterceptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // Entity types already reported as lacking database-enforced ownership; warned once per process.
    private static readonly ConcurrentDictionary<Type, byte> UnenforcedOwnershipReported = new();

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
        LogTenantScopedWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyException(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        LogTenantScopedWriteMatchedNoRow(eventData);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    // A tenant-scoped UPDATE or DELETE that affects no row is either an ordinary concurrency conflict or
    // an attempt to write another tenant's row with a forged TenantId. The two cannot be told apart
    // without another query, so EF Core's DbUpdateConcurrencyException is left as is and logged here.
    private void LogTenantScopedWriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        foreach (var entry in eventData.Entries)
        {
            if (entry.Entity is ITenantScoped<TKey>)
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

        if (!tenantContext.HasTenant)
        {
            HandleMissingTenant();
            return;
        }

        // In strict mode, validate before stamping — catches Added entities with an explicit
        // wrong TenantId (spoofing attempts) that would otherwise be silently overwritten.
        if (options.DetectSpoofedWrites)
        {
            spoofValidator.Validate(context.ChangeTracker.Entries(), tenantContext);
        }

        TenantWriteIsolationApplier.Apply(context.ChangeTracker.Entries(), tenantContext, diagnostics =>
        {
            logger.LogError(
                "Tenant isolation violation: entity '{EntityType}' belongs to tenant '{OffendingTenantId}' " +
                "but current scope is tenant '{ExpectedTenantId}'. Aborting SaveChanges",
                diagnostics.EntityTypeName,
                diagnostics.OffendingTenantId,
                diagnostics.ExpectedTenantId);
        },
        entry =>
        {
            if (UnenforcedOwnershipReported.TryAdd(entry.Metadata.ClrType, 0))
            {
                logger.LogWarning(
                    "Entity '{EntityType}' implements ITenantScoped but its TenantId is not a concurrency token, " +
                    "so UPDATE and DELETE statements do not check the stored tenant. Call " +
                    "modelBuilder.ApplyTenantFilters(...) in OnModelCreating",
                    entry.Metadata.ClrType.Name);
            }
        });
    }

    private void HandleMissingTenant()
    {
        switch (options.OnMissingTenant)
        {
            case MissingTenantBehavior.Reject:
                throw new TenantNotResolvedException(
                    "SaveChanges was called without a resolved tenant context and " +
                    "EfCoreIsolationOptions.OnMissingTenant is set to Reject. " +
                    "Ensure app.UseTenantry() (or a manual tenant scope) is active before writing, " +
                    "or relax the policy to Allow/Warn for operations that intentionally run without a tenant.");

            case MissingTenantBehavior.Warn:
                logger.LogWarning(
                    "SaveChanges called without a resolved tenant context. " +
                    "Entities will not have TenantId stamped and cross-tenant validation is skipped. " +
                    "Ensure app.UseTenantry() is registered in the middleware pipeline");
                break;

            case MissingTenantBehavior.Allow:
            case MissingTenantBehavior.Skip:
            default:
                // Proceed silently — the save runs without a stamped tenant.
                break;
        }
    }
}
