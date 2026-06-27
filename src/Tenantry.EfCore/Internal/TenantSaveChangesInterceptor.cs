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
///   <item>Validates that no <c>Modified</c> or <c>Deleted</c> entity belongs to a different tenant.</item>
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
