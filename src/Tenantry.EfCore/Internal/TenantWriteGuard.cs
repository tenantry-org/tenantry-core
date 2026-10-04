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
/// tenant of an entity whose <c>TenantId</c> EF Core does not write after an insert, or of a deleted and added pair over
/// more than one table (see <c>ConfirmStoredTenant</c>, <see cref="StoredTenantQuery"/>). Where the tenant check of some
/// rows is another of the save's statements, <see cref="AtomicSave"/> keeps the save all-or-nothing.
/// </remarks>
internal sealed class TenantWriteGuard<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IReadOnlyList<EntityEntry> _entries;
    private readonly TKey _tenantId;
    private readonly ILogger _logger;
    private readonly SaveWithoutTransactionBehavior _withoutTransaction;

    // The tracked entries by their values of each key an ownership names, built on first use.
    private readonly Dictionary<IKey, Dictionary<KeyValues, EntityEntry>> _byKey = [];

    // Whether the context's provider is relational: only then is an entity mapped to tables.
    private readonly bool _isRelational;

    // Entities whose stored tenant is read before the save, by entity.
    private readonly Dictionary<object, EntityEntry> _rowsToRead = new(ReferenceEqualityComparer.Instance);

    // The entities the save both deletes and adds under one key, each with whether its pair spans tables, found on
    // first use.
    private Dictionary<object, bool>? _sharedIdentities;

    // Owned entities whose owner is checked once every new entity is stamped: a new owner's key can include its
    // TenantId, which its owned entities' foreign keys take when it is stamped.
    private readonly List<EntityEntry> _ownedWrites = [];

    // Deleted entities, and new ones over more than one table, not keyed by their TenantId: checked once every new
    // entity is stamped, for whether the save deletes and adds one under the same key.
    private readonly List<EntityEntry> _deletesAndInserts = [];

    // The entities whose UPDATE or DELETE checks the tenant of rows other statements of the save write, by entity.
    private readonly HashSet<object> _checks = new(ReferenceEqualityComparer.Instance);

    // Whether a new entity's INSERT checks the tenant of rows other statements write.
    private bool _insertsAreChecks;

    private TenantWriteGuard(
        IReadOnlyList<EntityEntry> entries,
        TKey tenantId,
        ILogger logger,
        bool isRelational,
        SaveWithoutTransactionBehavior withoutTransaction)
    {
        _entries = entries;
        _isRelational = isRelational;
        _tenantId = tenantId;
        _logger = logger;
        _withoutTransaction = withoutTransaction;
    }

    /// <summary>Stamps and checks the tenant-owned entities <paramref name="context"/> is about to save.</summary>
    /// <exception cref="TenantIsolationViolationException">An entry belongs to, or names, another tenant.</exception>
    /// <exception cref="TenantNotResolvedException">No tenant is current and <see cref="EfCoreIsolationOptions.OnMissingTenant"/> rejects the write, or a new entity has no tenant.</exception>
    public static void Check(DbContext context)
    {
        AtomicSave.Start(context);

        try
        {
            if (Begin(context) is not { } guard)
            {
                return;
            }

            foreach (var row in guard._rowsToRead.Values)
            {
                guard.CheckStoredTenant(row, StoredTenantQuery.For(context, row, guard._tenantId)?.FirstOrDefault());
            }

            guard.End(context);
        }
        catch
        {
            // EF Core raises no failure for a save that a SavingChanges interceptor stops.
            AtomicSave.Failed(context);
            throw;
        }
    }

    /// <inheritdoc cref="Check"/>
    public static async Task CheckAsync(DbContext context, CancellationToken cancellationToken)
    {
        AtomicSave.Start(context);

        try
        {
            if (Begin(context) is not { } guard)
            {
                return;
            }

            foreach (var row in guard._rowsToRead.Values)
            {
                var stored = StoredTenantQuery.For(context, row, guard._tenantId) is { } query
                    ? await query.FirstOrDefaultAsync(cancellationToken)
                    : null;

                guard.CheckStoredTenant(row, stored);
            }

            guard.End(context);
        }
        catch
        {
            AtomicSave.Failed(context);
            throw;
        }
    }

    // Checks the change tracker, and returns the guard when a tenant is current, with the rows left to read.
    private static TenantWriteGuard<TKey>? Begin(DbContext context)
    {
        var services = ApplicationServices.Find(context);
        var tenantContext = ApplicationServices.TenantContext<TKey>(context);
        var logger = TenantIsolationLog.Find(services) ?? NullLogger.Instance;

        // Entries() runs DetectChanges once; the checks below read this list.
        var entries = context.ChangeTracker.Entries().ToList();

        var options = ApplicationServices.Isolation(context, services);

        if (!tenantContext.HasTenant)
        {
            CheckWithoutTenant(entries, options.OnMissingTenant, logger);
            return null;
        }

        var guard = new TenantWriteGuard<TKey>(
            entries,
            tenantContext.CurrentTenant!.TenantId,
            logger,
            context.Database.IsRelational(),
            options.OnSaveWithoutTransaction);

        guard.CheckEntries();
        return guard;
    }

    // Once every check has passed, so a rejected save leaves the context's settings as they were.
    private void End(DbContext context) =>
        AtomicSave.Guard(context, _checks, _insertsAreChecks, _withoutTransaction, _logger);

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
                    $"SaveChanges is writing tenant-owned entities ({entityTypes}) without a resolved tenant. " +
                    "Run the write while a tenant is current (app.UseTenantry() for requests, " +
                    "ITenantScopeFactory.RunInScopeAsync or CreateScope elsewhere). Maintenance code that deliberately " +
                    "writes across tenants can use a context of its own, registered with " +
                    "UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow).");
        }

        // Even when unscoped writes are allowed, a new row must name its tenant: an unowned row is never
        // visible through the tenant filter and belongs to no one.
        var unowned = writes.FirstOrDefault(entry =>
            entry is { State: EntityState.Added, Entity: ITenantEntity<TKey> entity } &&
            TenantIds.IsReserved(entity.TenantId));

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

        foreach (var entry in _deletesAndInserts)
        {
            CheckDeleteOrInsert(entry);
        }

        foreach (var owned in _ownedWrites)
        {
            CheckOwner(owned);
        }
    }

    private void CheckDeleteOrInsert(EntityEntry entry)
    {
        // A save that deletes an entity and adds one under the same key saves the pair as an UPDATE of what differs
        // between them, table by table, which for a pair over more than one table can leave out TenantId's table and so
        // its token: the deleted one's stored row is read. (On one table, the UPDATE carries the deleted one's token.)
        if (SharesIdentity(entry, out var spansTables))
        {
            if (spansTables && entry.State == EntityState.Deleted)
            {
                _rowsToRead.TryAdd(entry.Entity, entry);
            }
        }

        // One over more than one table is deleted from each, and only the DELETE from TenantId's table checks its
        // token: the others rely on it. It is inserted into each too, and only the INSERT into TenantId's table fails
        // on a key another tenant's row has (EF Core sends the others whatever the database answers), unless the
        // database generates the key.
        else if (SpansTables(entry.Metadata))
        {
            if (entry.State == EntityState.Deleted)
            {
                _checks.Add(entry.Entity);
            }
            else if (!HasTemporaryKey(entry))
            {
                _insertsAreChecks = true;
            }
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
                CheckChanged(entry, entity);
                break;

            case EntityState.Deleted:
                CheckChanged(entry, entity);

                // Keyed by its TenantId, every statement that names the row names its tenant.
                if (!IsKeyedByTenant(entry))
                {
                    _deletesAndInserts.Add(entry);
                }

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
        if (TenantIds.IsReserved(entity.TenantId))
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

        if (!IsKeyedByTenant(entry) && SpansTables(entry.Metadata))
        {
            _deletesAndInserts.Add(entry);
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

        // An entity mapped to more than one table (TPT, entity splitting) is updated only in the tables whose columns
        // changed, and only the table with TenantId checks its token, so a change to another table's columns would
        // match the row by its key: its stored tenant is confirmed as well. Keyed by its TenantId, every table's UPDATE
        // matches it already, and a change to TenantId's table alone carries the token itself.
        if (entry.State == EntityState.Modified &&
            !IsKeyedByTenant(entry) &&
            SpansTables(entry.Metadata) &&
            WritesTablesWithoutTenantId(entry))
        {
            ConfirmStoredTenant(entry, isCheck: true);
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
                CheckOwnerTenant(principal, ownership, owned);
                return;
            }

            dependent = principal;
        }
    }

    // A tenant-owned owner whose own INSERT, UPDATE or DELETE carries its TenantId is checked as an entry of its own.
    // One the save does not write (only loaded or attached, or modified with nothing EF Core writes) must have been
    // loaded or attached as the current tenant, and its stored row must be the current tenant's too: marking its
    // TenantId as modified makes EF Core write it back with the token in the same save, so the UPDATE matches only if
    // the row is the current tenant's, and otherwise the save fails with DbUpdateConcurrencyException and nothing is
    // written (AtomicSave). A TenantId in the key the owned entities are owned through
    // needs neither: their foreign key then names the current tenant, so they can only join that tenant's row. Any
    // other key they are owned through is the owner's primary key (TenantEntityTypes.ThrowIfOwnershipIsUnchecked), the
    // row the write-back matches. A TenantId EF Core does not write after an insert (in another key, which EF Core
    // does not let change, or configured so) is not written back: the stored row is read before the save instead.
    // Owned rows outside the table with the owner's TenantId are written by statements of their own, which rely on the
    // owner's: they are kept all-or-nothing with it (AtomicSave).
    private void CheckOwnerTenant(EntityEntry owner, IForeignKey ownership, EntityEntry owned)
    {
        var tenantId = owner.Metadata.FindProperty(TenantOwnership.TenantIdProperty)!;
        var namesTenant = ownership.PrincipalKey.Properties.Contains(tenantId);
        var reliesOnOwner = !namesTenant && HasStatementsOfItsOwn(owned, owner, tenantId);

        switch (owner.State)
        {
            // A save that deletes an entity and adds one under the same key saves the pair as one UPDATE of what
            // differs between them, which can be nothing, so no statement carries the token: the stored row is read.
            case EntityState.Added or EntityState.Deleted when SharesIdentity(owner):
                _rowsToRead.TryAdd(owner.Entity, owner);
                return;

            // Its DELETE checks the tenant of the owned rows deleted with it.
            case EntityState.Deleted:
                if (reliesOnOwner)
                {
                    _checks.Add(owner.Entity);
                }

                return;

            // Its INSERT fails on a key another tenant's row has, unless the database generates it.
            case EntityState.Added:
                if (reliesOnOwner && !HasTemporaryKey(owner))
                {
                    _insertsAreChecks = true;
                }

                return;

            case EntityState.Modified when IsWritten(owner):
                if (reliesOnOwner)
                {
                    _checks.Add(owner.Entity);
                }

                return;
        }

        var ownerTenantId = TenantOwnership.OriginalTenantId<TKey>(owner);

        if (!TenantOwnership.IsOwnedBy(ownerTenantId, _tenantId))
        {
            Violation(owner, Display(ownerTenantId));
        }

        if (namesTenant)
        {
            return;
        }

        ConfirmStoredTenant(owner, reliesOnOwner);
    }

    // Makes the save confirm that an entry's stored row is the current tenant's: its TenantId is written back with its
    // token, or, when EF Core does not write TenantId after an insert, the row is read before the save. A write-back
    // that other statements rely on is a check (AtomicSave).
    private void ConfirmStoredTenant(EntityEntry entry, bool isCheck)
    {
        if (entry.Metadata.FindProperty(TenantOwnership.TenantIdProperty)!.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
        {
            _rowsToRead.TryAdd(entry.Entity, entry);
            return;
        }

        entry.Property(TenantOwnership.TenantIdProperty).IsModified = true;

        if (isCheck)
        {
            _checks.Add(entry.Entity);
        }
    }

    // Whether an owned entry's rows are written by statements other than the one that writes its owner's TenantId: they
    // are in another table. A provider that maps no tables writes each entity apart.
    private bool HasStatementsOfItsOwn(EntityEntry owned, EntityEntry owner, IProperty tenantId)
    {
        if (!_isRelational)
        {
            return true;
        }

        var tenantTables = TenantIdTables(owner.Metadata, tenantId);
        return owned.Metadata.GetTableMappings().Any(mapping => !tenantTables.Contains(mapping.Table));
    }

    // Whether a modified entry writes a table its TenantId is not in, whose UPDATE carries no token. A changed complex
    // property counts, whatever table it is in.
    private static bool WritesTablesWithoutTenantId(EntityEntry entry)
    {
        var tenantTables = TenantIdTables(entry.Metadata, entry.Metadata.FindProperty(TenantOwnership.TenantIdProperty)!);

        return entry.Members.Any(member => member switch
        {
            PropertyEntry property =>
                property.IsModified &&
                property.Metadata.GetAfterSaveBehavior() == PropertySaveBehavior.Save &&
                property.Metadata.GetTableColumnMappings().Any(column => !tenantTables.Contains(column.TableMapping.Table)),
            NavigationEntry => false,
            _ => member.IsModified,
        });
    }

    // The tables an entity type's TenantId is mapped to: those whose statements carry its token.
    private static List<ITable> TenantIdTables(IEntityType entityType, IProperty tenantId) =>
        [.. entityType.GetTableMappings()
            .Where(mapping => mapping.ColumnMappings.Any(column => column.Property == tenantId))
            .Select(mapping => mapping.Table)];

    // Whether the database generates the entry's key, which no other row then has.
    private static bool HasTemporaryKey(EntityEntry entry) =>
        entry.Metadata.FindPrimaryKey()?.Properties.Any(property => entry.Property(property.Name).IsTemporary) == true;

    private bool SpansTables(IEntityType entityType) =>
        _isRelational && entityType.GetTableMappings().Select(mapping => mapping.Table).Distinct().Skip(1).Any();

    // Whether the entry's primary key includes its TenantId, so every statement that names the row names its tenant.
    private static bool IsKeyedByTenant(EntityEntry entry) =>
        entry.Metadata.FindProperty(TenantOwnership.TenantIdProperty)?.IsPrimaryKey() == true;

    // Whether EF Core writes a modified entry's row: only properties saved after an insert are, so one whose only
    // changes are to keys, values the database generates or properties configured not to be saved sends no UPDATE.
    private static bool IsWritten(EntityEntry entry) =>
        entry.Properties.Any(property => property.IsModified && property.Metadata.GetAfterSaveBehavior() == PropertySaveBehavior.Save);

    private bool SharesIdentity(EntityEntry entry) => SharesIdentity(entry, out _);

    private bool SharesIdentity(EntityEntry entry, out bool spansTables)
    {
        _sharedIdentities ??= FindSharedIdentities();
        return _sharedIdentities.TryGetValue(entry.Entity, out spansTables);
    }

    // The entities the save both deletes and adds under one key.
    private Dictionary<object, bool> FindSharedIdentities()
    {
        Dictionary<object, bool> shared = new(ReferenceEqualityComparer.Instance);
        var pairs = _entries
            .Where(entry => entry.State is EntityState.Added or EntityState.Deleted && entry.Metadata.FindPrimaryKey() is not null)
            .GroupBy(entry => (entry.Metadata.GetRootType(), KeyValues.Of(entry, entry.Metadata.FindPrimaryKey()!.Properties)));

        foreach (var pair in pairs)
        {
            if (pair.Any(entry => entry.State == EntityState.Added) && pair.Any(entry => entry.State == EntityState.Deleted))
            {
                var spansTables = pair.Any(entry => SpansTables(entry.Metadata));

                foreach (var entry in pair)
                {
                    shared.TryAdd(entry.Entity, spansTables);
                }
            }
        }

        return shared;
    }

    // An entity's stored TenantId, read only if it is the current tenant's (StoredTenantQuery): null when no row with
    // its key is. It is compared again here as the in-memory checks compare it.
    private void CheckStoredTenant(EntityEntry entry, object? stored)
    {
        if (stored is TKey storedTenantId && TenantOwnership.IsOwnedBy(storedTenantId, _tenantId))
        {
            return;
        }

        var typeName = entry.Entity.GetType().Name;
        Violation(
            entry,
            offendingTenantId: null,
            $"Tenant isolation violation on entity '{typeName}': no row with its key is stored for the current tenant " +
            $"'{_tenantId}'. SaveChanges was aborted before anything was written. Change an entity, or the entities it " +
            "owns, only while its own tenant is current.");
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

        return byValues.TryGetValue(KeyValues.Of(dependent, ownership.Properties, key.Properties), out var principal)
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
    // Compared as EF Core compares key values, with each key property's comparer (a byte array by its contents).
    private sealed class KeyValues : IEquatable<KeyValues>
    {
        private readonly object?[] _values;
        private readonly ValueComparer[] _comparers;

        private KeyValues(object?[] values, ValueComparer[] comparers)
        {
            _values = values;
            _comparers = comparers;
        }

        // The values of an entry's properties, compared as the key's values are.
        public static KeyValues Of(EntityEntry entry, IReadOnlyList<IProperty> properties, IReadOnlyList<IProperty> key) =>
            new(
                [.. properties.Select(property => entry.Property(property.Name).CurrentValue)],
                [.. key.Select(property => property.GetKeyValueComparer())]);

        public static KeyValues Of(EntityEntry entry, IReadOnlyList<IProperty> key) => Of(entry, key, key);

        public bool Equals(KeyValues? other)
        {
            if (other is null || other._values.Length != _values.Length)
            {
                return false;
            }

            for (var i = 0; i < _values.Length; i++)
            {
                if (!_comparers[i].Equals(_values[i], other._values[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as KeyValues);

        public override int GetHashCode()
        {
            HashCode hash = new();

            for (var i = 0; i < _values.Length; i++)
            {
                hash.Add(_values[i] is null ? 0 : _comparers[i].GetHashCode(_values[i]));
            }

            return hash.ToHashCode();
        }
    }
}
