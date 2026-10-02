using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Checks what a <c>SaveChanges</c> is about to write, in one pass over the change tracker: it stamps new tenant-owned
/// entities with the current tenant and rejects writes of another tenant's entities, or, with no tenant current,
/// applies <see cref="EfCoreIsolationOptions.OnMissingTenant"/>.
/// </summary>
/// <remarks>
/// A violation is logged (event 2001) and thrown before anything is written. The only query it runs reads the stored
/// tenant of an owner whose <c>TenantId</c> EF Core does not write after an insert (see <c>CheckOwnerTenant</c>).
/// </remarks>
internal sealed class TenantWriteGuard<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IReadOnlyList<EntityEntry> _entries;
    private readonly TKey _tenantId;
    private readonly ILogger _logger;

    // The tracked entries by their values of each key an ownership names, built on first use.
    private readonly Dictionary<IKey, Dictionary<KeyValues, EntityEntry>> _byKey = [];

    // Owners whose stored tenant is read before the save, by entity.
    private readonly Dictionary<object, EntityEntry> _ownersToRead = new(ReferenceEqualityComparer.Instance);

    // The entities the save both deletes and adds under one key, found on first use.
    private HashSet<object>? _sharedIdentities;

    // Owned entities whose owner is checked once every new entity is stamped: a new owner's key can include its
    // TenantId, which its owned entities' foreign keys take when it is stamped.
    private readonly List<EntityEntry> _ownedWrites = [];

    private TenantWriteGuard(IReadOnlyList<EntityEntry> entries, TKey tenantId, ILogger logger)
    {
        _entries = entries;
        _tenantId = tenantId;
        _logger = logger;
    }

    /// <summary>Stamps and checks the tenant-owned entities <paramref name="context"/> is about to save.</summary>
    /// <exception cref="TenantIsolationViolationException">An entry belongs to, or names, another tenant.</exception>
    /// <exception cref="TenantNotResolvedException">No tenant is current and <see cref="EfCoreIsolationOptions.OnMissingTenant"/> rejects the write, or a new entity has no tenant.</exception>
    public static void Check(DbContext context)
    {
        if (Begin(context) is not { } guard)
        {
            return;
        }

        foreach (var owner in guard._ownersToRead.Values)
        {
            guard.CheckStoredTenant(owner, owner.GetDatabaseValues());
        }
    }

    /// <inheritdoc cref="Check"/>
    public static async Task CheckAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (Begin(context) is not { } guard)
        {
            return;
        }

        foreach (var owner in guard._ownersToRead.Values)
        {
            guard.CheckStoredTenant(owner, await owner.GetDatabaseValuesAsync(cancellationToken));
        }
    }

    // Checks the change tracker, and returns the guard when a tenant is current, with the owners left to read.
    private static TenantWriteGuard<TKey>? Begin(DbContext context)
    {
        var services = ApplicationServices.Find(context);
        var tenantContext = ApplicationServices.TenantContext<TKey>(context);
        var logger = TenantIsolationLog.Find(services) ?? NullLogger.Instance;

        // Entries() runs DetectChanges once; the checks below read this list.
        var entries = context.ChangeTracker.Entries().ToList();

        if (!tenantContext.HasTenant)
        {
            var behavior = services?.GetService<EfCoreIsolationOptions>()?.OnMissingTenant ?? MissingTenantBehavior.Reject;
            CheckWithoutTenant(entries, behavior, logger);
            return null;
        }

        var guard = new TenantWriteGuard<TKey>(entries, tenantContext.CurrentTenant!.TenantId, logger);
        guard.CheckEntries();
        return guard;
    }

    // A tenant-owned UPDATE or DELETE that affects no row is either an ordinary concurrency conflict or an attempt to
    // write another tenant's row with a forged TenantId. The two cannot be told apart without another query, so EF
    // Core's DbUpdateConcurrencyException is left as it is and logged here. Logging never replaces that exception.
    public static void WriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        if (eventData.Context is not { } context ||
            ApplicationServices.Find(context) is not { } services ||
            TenantIsolationLog.Find(services) is not { } logger)
        {
            return;
        }

        var tenantContext = services.GetService<ITenantContext<TKey>>();
        var tenantId = tenantContext is { HasTenant: true } ? tenantContext.CurrentTenantId?.ToString() : null;

        foreach (var entry in eventData.Entries.Where(entry => entry.Entity is ITenantEntity<TKey>))
        {
            TenantIsolationLog.WriteMatchedNoRow(logger, entry.State.ToString(), entry.Entity.GetType().Name, tenantId);
        }
    }

    private static void CheckWithoutTenant(IReadOnlyList<EntityEntry> entries, MissingTenantBehavior behavior, ILogger logger)
    {
        var writes = entries.Where(IsTenantWrite).ToList();

        // A save that writes no tenant-owned entity (e.g. a host-level catalogue) needs no tenant.
        if (writes.Count == 0)
        {
            return;
        }

        var entityTypes = string.Join(", ", writes.Select(entry => entry.Metadata.ClrType.Name).Distinct());

        switch (behavior)
        {
            case MissingTenantBehavior.Warn:
                TenantIsolationLog.WriteWithoutTenant(logger, entityTypes);
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
        var unowned = writes.FirstOrDefault(entry =>
            entry is { State: EntityState.Added, Entity: ITenantEntity<TKey> entity } &&
            TenantOwnership.IsUnstamped(entity.TenantId));

        if (unowned is not null)
        {
            throw new TenantNotResolvedException(
                $"A new '{unowned.Metadata.ClrType.Name}' is being saved without a resolved tenant and without a " +
                "TenantId. Set TenantId explicitly or save it while its tenant is current.");
        }
    }

    private void CheckEntries()
    {
        foreach (var entry in _entries)
        {
            if (entry.Entity is ITenantEntity<TKey> entity)
            {
                CheckTenantEntity(entry, entity);
            }
            else if (entry.Metadata.IsOwned() && IsWrite(entry))
            {
                // An owned type without a TenantId of its own: its rows carry no tenant, only their owner's key.
                _ownedWrites.Add(entry);
            }
        }

        foreach (var owned in _ownedWrites)
        {
            CheckOwner(owned);
        }
    }

    private void CheckTenantEntity(EntityEntry entry, ITenantEntity<TKey> entity)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                CheckNew(entry, entity);
                break;

            case EntityState.Modified:
            case EntityState.Deleted:
                CheckChanged(entry, entity);
                break;

            case EntityState.Detached:
            case EntityState.Unchanged:
            default:
                break;
        }
    }

    // A new entity is saved for the current tenant. One that already names another tenant is rejected rather than
    // silently moved: the caller meant another tenant's data.
    private void CheckNew(EntityEntry entry, ITenantEntity<TKey> entity)
    {
        if (TenantOwnership.IsUnstamped(entity.TenantId))
        {
            // Through EF Core rather than the entity, so a private or init-only setter works.
            entry.Property(TenantOwnership.TenantIdProperty).CurrentValue = _tenantId;
        }
        else if (!TenantOwnership.IsOwnedBy(entity.TenantId, _tenantId))
        {
            Violation(
                entry,
                Display(entity.TenantId),
                $"A new '{entry.Entity.GetType().Name}' names tenant '{entity.TenantId}', but the current tenant is " +
                $"'{_tenantId}'. Leave TenantId unset on new entities: they are saved for the current tenant.");
        }

        if (entry.Metadata.IsOwned())
        {
            _ownedWrites.Add(entry);
        }
    }

    // The row must have been loaded or attached as the current tenant (the original value, which becomes the
    // UPDATE/DELETE predicate) and must still belong to it (the current value). Checking both rejects moving a row
    // between tenants and writing an entity that was loaded under another tenant's scope.
    private void CheckChanged(EntityEntry entry, ITenantEntity<TKey> entity)
    {
        var originalTenantId = TenantOwnership.OriginalTenantId<TKey>(entry);

        if (!TenantOwnership.IsOwnedBy(originalTenantId, _tenantId))
        {
            Violation(entry, Display(originalTenantId));
        }

        if (!TenantOwnership.IsOwnedBy(entity.TenantId, _tenantId))
        {
            Violation(entry, Display(entity.TenantId));
        }

        // An owned entity with a key of its own moves to another owner when its foreign key changes, and EF Core reads
        // it through whichever owner that names, so the owner it moves to is checked as one it is added to would be.
        if (entry.State == EntityState.Modified &&
            entry.Metadata.FindOwnership() is { } ownership &&
            ownership.Properties.Any(property => entry.Property(property).IsModified))
        {
            _ownedWrites.Add(entry);
        }
    }

    // An owned entity belongs to its owner, and EF Core reads owned rows through the owner without a tenant filter of
    // their own. An owner that is only attached (Unchanged) is not written, so its TenantId concurrency token is never
    // checked, and a caller could attach a stub of another tenant's owner (with a forged TenantId) and add rows that
    // tenant then reads, or, for an owned type with no TenantId of its own, change or delete its rows. So the owned
    // entity's nearest tenant-owned owner is checked: the owned types in between have no TenantId, so their keys
    // include their owners' (TenantEntityTypes.ThrowIfOwnershipIsUnchecked), and the owned entity's foreign key names that
    // owner's row. An owned entity saved without its owner is rejected.
    private void CheckOwner(EntityEntry owned)
    {
        // Owned by an entity type that is not tenant-owned: nothing to isolate.
        if (!IsTenantEntity(RootOwnerType(owned.Metadata)))
        {
            return;
        }

        var dependent = owned;

        while (dependent.Metadata.FindOwnership() is { } ownership)
        {
            var principal = Principal(dependent, ownership);

            if (principal is null)
            {
                var typeName = owned.Entity.GetType().Name;
                Violation(
                    owned,
                    offendingTenantId: null,
                    $"A '{typeName}' is being saved without its owner '{ownership.PrincipalEntityType.ClrType.Name}'. " +
                    "Tenantry checks an owned entity's tenant through its owner, so load or attach the owner in the same " +
                    "context and save the owned entity through it.");
            }

            if (principal.Entity is ITenantEntity<TKey>)
            {
                CheckOwnerTenant(principal, ownership);
                return;
            }

            dependent = principal;
        }
    }

    // A tenant-owned owner whose own INSERT, UPDATE or DELETE carries its TenantId is checked as an entry of its own.
    // One the save does not write (only loaded or attached, or modified with nothing EF Core writes) must have been
    // loaded or attached as the current tenant, and its stored row must be the current tenant's too: marking its
    // TenantId as modified makes EF Core write it back with the token in the same transaction, so the UPDATE matches
    // only if the row is the current tenant's, and otherwise the save fails with DbUpdateConcurrencyException and
    // nothing is written. A TenantId in the key the owned entities are owned through
    // needs neither: their foreign key then names the current tenant, so they can only join that tenant's row. Any
    // other key they are owned through is the owner's primary key (TenantEntityTypes.ThrowIfOwnershipIsUnchecked), the
    // row the write-back matches. A TenantId EF Core does not write after an insert (in another key, which EF Core
    // does not let change, or configured so) is not written back: the stored row is read through the tenant filter
    // before the save instead.
    private void CheckOwnerTenant(EntityEntry owner, IForeignKey ownership)
    {
        switch (owner.State)
        {
            // A save that deletes an entity and adds one under the same key saves the pair as one UPDATE of what
            // differs between them, which can be nothing, so no statement carries the token: the stored row is read.
            case EntityState.Added or EntityState.Deleted:
                if (SharesIdentity(owner))
                {
                    _ownersToRead.TryAdd(owner.Entity, owner);
                }

                return;

            case EntityState.Modified when IsWritten(owner):
                return;
        }

        var ownerTenantId = TenantOwnership.OriginalTenantId<TKey>(owner);

        if (!TenantOwnership.IsOwnedBy(ownerTenantId, _tenantId))
        {
            Violation(owner, Display(ownerTenantId));
        }

        var tenantId = owner.Metadata.FindProperty(TenantOwnership.TenantIdProperty)!;

        if (ownership.PrincipalKey.Properties.Contains(tenantId))
        {
            return;
        }

        if (tenantId.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
        {
            _ownersToRead.TryAdd(owner.Entity, owner);
            return;
        }

        owner.Property(TenantOwnership.TenantIdProperty).IsModified = true;
    }

    // Whether EF Core writes a modified entry's row: only properties saved after an insert are, so one whose only
    // changes are to keys, values the database generates or properties configured not to be saved sends no UPDATE.
    private static bool IsWritten(EntityEntry entry) =>
        entry.Properties.Any(property => property.IsModified && property.Metadata.GetAfterSaveBehavior() == PropertySaveBehavior.Save);

    private bool SharesIdentity(EntityEntry entry)
    {
        _sharedIdentities ??= FindSharedIdentities();
        return _sharedIdentities.Contains(entry.Entity);
    }

    // The entities the save both deletes and adds under one key.
    private HashSet<object> FindSharedIdentities()
    {
        HashSet<object> shared = new(ReferenceEqualityComparer.Instance);
        var pairs = _entries
            .Where(entry => entry.State is EntityState.Added or EntityState.Deleted && entry.Metadata.FindPrimaryKey() is not null)
            .GroupBy(entry => (entry.Metadata.GetRootType(), KeyValues.Of(entry, entry.Metadata.FindPrimaryKey()!.Properties)));

        foreach (var pair in pairs)
        {
            if (pair.Any(entry => entry.State == EntityState.Added) && pair.Any(entry => entry.State == EntityState.Deleted))
            {
                shared.UnionWith(pair.Select(entry => entry.Entity));
            }
        }

        return shared;
    }

    // An owner's stored values, read through the tenant filter, which Tenantry keeps on EF Core's database-values
    // query: null when no row with its key is the current tenant's.
    private void CheckStoredTenant(EntityEntry owner, PropertyValues? stored)
    {
        if (stored?[TenantOwnership.TenantIdProperty] is TKey storedTenantId && TenantOwnership.IsOwnedBy(storedTenantId, _tenantId))
        {
            return;
        }

        var typeName = owner.Entity.GetType().Name;
        Violation(
            owner,
            offendingTenantId: null,
            $"The '{typeName}' that owns entities being saved is not stored for the current tenant '{_tenantId}'. " +
            "SaveChanges was aborted before anything was written. Save owned entities only through an owner of the " +
            "current tenant.");
    }

    // An ownership's principal: the tracked entry with the dependent's foreign key values in the key the ownership
    // names, from an index of the entries built on first use, so a save with many owned entries does not scan the
    // change tracker for each.
    private EntityEntry? Principal(EntityEntry dependent, IForeignKey ownership)
    {
        var key = ownership.PrincipalKey;

        if (!_byKey.TryGetValue(key, out var byValues))
        {
            byValues = [];

            foreach (var entry in _entries)
            {
                if (key.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                {
                    byValues.TryAdd(KeyValues.Of(entry, key.Properties), entry);
                }
            }

            _byKey.Add(key, byValues);
        }

        return byValues.TryGetValue(KeyValues.Of(dependent, ownership.Properties), out var principal)
               && ownership.PrincipalEntityType.IsAssignableFrom(principal.Metadata)
            ? principal
            : null;
    }

    // A write of a tenant-owned entity, or of an owned entity whose owner is tenant-owned: its rows are that tenant's
    // even when its type has no TenantId of its own.
    private static bool IsTenantWrite(EntityEntry entry) =>
        IsWrite(entry) &&
        (entry.Entity is ITenantEntity<TKey> || (entry.Metadata.IsOwned() && IsTenantEntity(RootOwnerType(entry.Metadata))));

    private static bool IsWrite(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static bool IsTenantEntity(IReadOnlyEntityType entityType) =>
        typeof(ITenantEntity<TKey>).IsAssignableFrom(entityType.ClrType);

    // The first entity type up an owned type's ownership chain that is not itself owned.
    private static IReadOnlyEntityType RootOwnerType(IReadOnlyEntityType entityType)
    {
        while (entityType.IsOwned() && entityType.FindOwnership() is { } ownership)
        {
            entityType = ownership.PrincipalEntityType;
        }

        return entityType;
    }

    private static string Display(TKey? tenantId) => tenantId?.ToString() ?? "<null>";

    [DoesNotReturn]
    private void Violation(EntityEntry entry, string? offendingTenantId, string? message = null)
    {
        var typeName = entry.Entity.GetType().Name;
        var expected = _tenantId.ToString();

        TenantIsolationLog.IsolationViolation(_logger, typeName, offendingTenantId, expected);

        throw new TenantIsolationViolationException(
            TenantIsolationViolationKind.EntityWrite,
            typeName,
            message ??
            $"Tenant isolation violation on entity '{typeName}': it belongs to tenant '{offendingTenantId}' but the " +
            $"current tenant is '{expected}'. SaveChanges was aborted before anything was written. Change an entity " +
            "only while its own tenant is current.",
            offendingTenantId,
            expected);
    }

    // The values of an entry's key properties, compared by value.
    private sealed class KeyValues : IEquatable<KeyValues>
    {
        private readonly object?[] _values;

        private KeyValues(object?[] values) => _values = values;

        public static KeyValues Of(EntityEntry entry, IReadOnlyList<IProperty> properties) =>
            new([.. properties.Select(property => entry.Property(property.Name).CurrentValue)]);

        public bool Equals(KeyValues? other) => other is not null && _values.SequenceEqual(other._values);

        public override bool Equals(object? obj) => Equals(obj as KeyValues);

        public override int GetHashCode()
        {
            HashCode hash = new();

            foreach (var value in _values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
