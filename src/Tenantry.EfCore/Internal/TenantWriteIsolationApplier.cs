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
        var all = entries as IReadOnlyList<EntityEntry> ?? entries.ToList();

        foreach (var entry in all)
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

                    if (entry.Metadata.IsOwned())
                    {
                        CheckOwner(entry, all, currentTenantId, onViolation);
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

    // A new owned entity is stamped with the current tenant, but it belongs to its owner, and EF Core reads owned rows
    // through the owner without a tenant filter of their own. An owner that is only attached (Unchanged) is not
    // written, so its TenantId concurrency token is never checked, and a caller could attach a stub of another
    // tenant's owner (with a forged TenantId) and add rows that tenant then reads. Marking the owner's TenantId as
    // modified makes EF Core write it back with the token in the same transaction: the UPDATE matches only if the row
    // is the current tenant's, and otherwise the save fails with DbUpdateConcurrencyException and nothing is written.
    // An owner attached as another tenant is rejected here, as a changed one would be. An owner keyed by TenantId needs
    // no check: the owned rows' key then carries the tenant.
    private static void CheckOwner<TKey>(
        EntityEntry owned,
        IReadOnlyList<EntityEntry> entries,
        TKey currentTenantId,
        Action<TenantIsolationViolationException>? onViolation)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var owner = RootOwner(owned, entries);

        if (owner is not { State: EntityState.Unchanged }
            || owner.Metadata.FindProperty(TenantOwnership.TenantIdProperty) is not { } tenantId
            || tenantId.IsKey())
        {
            return;
        }

        var ownerTenantId = TenantOwnership.OriginalTenantId<TKey>(owner);

        if (!TenantOwnership.IsOwnedBy(ownerTenantId, currentTenantId))
        {
            ThrowViolation(owner, ownerTenantId, currentTenantId, onViolation);
        }

        owner.Property(TenantOwnership.TenantIdProperty).IsModified = true;
    }

    // The tracked entry that owns an owned entry, following nested ownership to an entity type that is not owned.
    private static EntityEntry? RootOwner(EntityEntry entry, IReadOnlyList<EntityEntry> entries)
    {
        var current = entry;

        while (current.Metadata.IsOwned())
        {
            if (current.Metadata.FindOwnership() is not { } ownership)
            {
                return null;
            }

            var dependent = current;
            var key = ownership.Properties.Select(property => dependent.Property(property.Name).CurrentValue).ToArray();
            var principal = entries.FirstOrDefault(candidate =>
                ownership.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)
                && ownership.PrincipalKey.Properties
                    .Select(property => candidate.Property(property.Name).CurrentValue)
                    .SequenceEqual(key));

            if (principal is null)
            {
                return null;
            }

            current = principal;
        }

        return current;
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
