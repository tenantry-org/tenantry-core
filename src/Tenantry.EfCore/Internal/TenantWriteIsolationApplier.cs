using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Stamps new tenant-owned entities with the current tenant and rejects writes of another tenant's entities.
/// </summary>
internal static class TenantWriteIsolationApplier
{
    /// <summary>
    /// Applies write isolation to <paramref name="entries"/> while <paramref name="tenantContext"/> has a tenant.
    /// </summary>
    /// <param name="entries">The change tracker's entries.</param>
    /// <param name="tenantContext">The current tenant.</param>
    /// <param name="onViolation">Called with the exception before it is thrown, to log it.</param>
    /// <exception cref="TenantIsolationViolationException">An entry belongs to, or names, another tenant.</exception>
    public static void Apply<TKey>(
        IEnumerable<EntityEntry> entries,
        ITenantContext<TKey> tenantContext,
        Action<TenantIsolationViolationException>? onViolation = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (!tenantContext.HasTenant)
        {
            return;
        }

        var currentTenantId = tenantContext.CurrentTenant!.TenantId;

        foreach (var entry in entries)
        {
            if (entry.Entity is not ITenantEntity<TKey> tenantEntity)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    // A new entity is saved for the current tenant. One that already names another tenant is
                    // rejected rather than silently moved: the caller meant another tenant's data.
                    if (TenantOwnership.IsUnstamped(tenantEntity.TenantId))
                    {
                        // Through EF Core rather than the entity, so a private or init-only setter works.
                        entry.Property(TenantOwnership.TenantIdProperty).CurrentValue = currentTenantId;
                    }
                    else if (!TenantOwnership.IsOwnedBy(tenantEntity.TenantId, currentTenantId))
                    {
                        ThrowViolation(
                            entry,
                            tenantEntity.TenantId,
                            currentTenantId,
                            $"A new '{entry.Entity.GetType().Name}' names tenant '{tenantEntity.TenantId}', but the current " +
                            $"tenant is '{currentTenantId}'. Leave TenantId unset on new entities: they are saved for " +
                            "the current tenant.",
                            onViolation);
                    }

                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    // The row must have been loaded or attached as the current tenant (the original value,
                    // which becomes the UPDATE/DELETE predicate) and must still belong to it (the current
                    // value). Checking both rejects moving a row between tenants and writing an entity that
                    // was loaded under another tenant's scope.
                    var originalTenantId = TenantOwnership.OriginalTenantId<TKey>(entry);

                    if (!TenantOwnership.IsOwnedBy(originalTenantId, currentTenantId))
                    {
                        ThrowViolation(entry, originalTenantId, currentTenantId, onViolation);
                    }

                    if (!TenantOwnership.IsOwnedBy(tenantEntity.TenantId, currentTenantId))
                    {
                        ThrowViolation(entry, tenantEntity.TenantId, currentTenantId, onViolation);
                    }

                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }
    }

    [DoesNotReturn]
    private static void ThrowViolation<TKey>(
        EntityEntry entry,
        TKey? offendingTenantId,
        TKey currentTenantId,
        Action<TenantIsolationViolationException>? onViolation)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        ThrowViolation(
            entry,
            offendingTenantId,
            currentTenantId,
            $"Tenant isolation violation on entity '{entry.Entity.GetType().Name}': it belongs to tenant " +
            $"'{Display(offendingTenantId)}' but the current tenant is '{currentTenantId}'. SaveChanges was aborted " +
            "before anything was written. Change an entity only while its own tenant is current.",
            onViolation);

    [DoesNotReturn]
    private static void ThrowViolation<TKey>(
        EntityEntry entry,
        TKey? offendingTenantId,
        TKey currentTenantId,
        string message,
        Action<TenantIsolationViolationException>? onViolation)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        TenantIsolationViolationException violation = new(
            TenantIsolationViolationKind.EntityWrite,
            entry.Entity.GetType().Name,
            message,
            Display(offendingTenantId),
            currentTenantId.ToString());

        onViolation?.Invoke(violation);

        throw violation;
    }

    private static string Display<TKey>(TKey? tenantId) => tenantId?.ToString() ?? "<null>";
}
