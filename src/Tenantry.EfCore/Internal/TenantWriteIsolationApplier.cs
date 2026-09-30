using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

internal static class TenantWriteIsolationApplier
{
    public static void Apply<TKey>(
        IEnumerable<EntityEntry> entries,
        ITenantContext<TKey> tenantContext,
        Action<IsolationDiagnostics>? onViolation = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (!tenantContext.HasTenant)
        {
            return;
        }

        var currentTenantId = tenantContext.CurrentTenant!.TenantId;

        foreach (var entry in entries)
        {
            if (entry.Entity is not ITenantScoped<TKey> tenantEntity)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    tenantEntity.TenantId = currentTenantId;
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
        Action<IsolationDiagnostics>? onViolation)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        IsolationDiagnostics diagnostics = new()
        {
            EntityTypeName = entry.Entity.GetType().Name,
            OffendingTenantId = offendingTenantId?.ToString() ?? "<null>",
            ExpectedTenantId = currentTenantId.ToString()!,
        };

        onViolation?.Invoke(diagnostics);

        throw new TenantIsolationViolationException(
            diagnostics.EntityTypeName,
            diagnostics.OffendingTenantId,
            diagnostics.ExpectedTenantId);
    }
}
