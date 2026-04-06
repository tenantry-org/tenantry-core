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
                    if (tenantEntity.TenantId.Equals(currentTenantId))
                    {
                        break;
                    }

                    IsolationDiagnostics diagnostics = new()
                    {
                        EntityTypeName = entry.Entity.GetType().Name,
                        OffendingTenantId = tenantEntity.TenantId.ToString()!,
                        ExpectedTenantId = currentTenantId.ToString()!,
                    };

                    onViolation?.Invoke(diagnostics);

                    throw new TenantIsolationViolationException(
                        diagnostics.EntityTypeName,
                        diagnostics.OffendingTenantId,
                        diagnostics.ExpectedTenantId);

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }
    }
}