# Owned and multi-table entities

Owned rows in a table of their own, and the rows of an entity mapped to more than one table, have no tenant check of
their own. Tenantry checks them through another statement and keeps the save all-or-nothing. The join rows of a
many-to-many relationship have none either, so their join entity must be tenant-scoped.

## Owned entities

EF Core reads owned rows only through their owner and allows them no filter of their own, so an owned type is isolated
through its owner whether or not it implements `ITenantEntity<TKey>`. A tenant-scoped owned type's `TenantId` is still
a concurrency token.

Writes are checked through the nearest tenant-scoped owner. A save that adds an owned entity, moves one with its own
key to another owner (by changing its foreign key), or changes or deletes one with no `TenantId` of its own needs that
owner loaded or attached as the current tenant. The database then confirms the owner's tenant:

- An owner the save does not otherwise write (loaded or only attached, or modified with nothing EF Core writes) has
  its `TenantId` written back with its concurrency token, so an audit log sees an update of the owner.
- An owner deleted and added again under the same key, which EF Core saves as one `UPDATE` of what differs, has its
  stored row read.
- An owner whose `TenantId` is part of the key its owned types are owned through needs neither: their foreign key
  names the tenant.
- An owner whose `TenantId` EF Core does not write after an insert (it is part of another key, such as an alternate key
  on `(TenantId, Id)`, or is configured not to be saved) has its stored row read before the save, one query per owner.
  The read names the current tenant and ignores every query filter, yours too, as EF Core's own writes do, so an owner
  your filter hides (an archived one, say) can still be given owned entities.

An owned entity saved without its owner in the same context is rejected. With no tenant, `OnMissingTenant` treats
owned entities as tenant-scoped. Owned rows in their own table rely on the owner's statement, so the save must succeed
or fail as a whole (below).

## Entities mapped to more than one table

An entity mapped to more than one table (table-per-type inheritance, entity splitting) is updated only in the tables
whose columns changed. So when one changes, its `TenantId` is also written back to its table to be checked there, or,
when EF Core does not save `TenantId`, its stored row is read before the save. One keyed by its `TenantId` needs
neither: every table's key names the tenant. A save that deletes such an entity and adds one under the same key, which
EF Core saves as an `UPDATE` of what differs, table by table, has the deleted one's stored row read. Rows outside the
table with `TenantId` rely on that table's statement, so the save must succeed or fail as a whole.

## Many-to-many relationships

The join rows of a many-to-many relationship hold the keys of the two rows they join. EF Core inserts and deletes them
when a collection changes, while the entities at both ends stay unchanged and are not written, so no statement of the
save checks a `TenantId`. `UseTenantry()` therefore refuses a many-to-many relationship with a tenant-scoped type at
either end unless its join entity is tenant-scoped too. That includes the join entity EF Core creates when you configure
none, and a relationship whose other end is shared across tenants.

Give the relationship a join entity of your own that implements `ITenantEntity<TKey>`:

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class Post : TenantEntity<Guid>
{
    public int Id { get; set; }
    public List<Tag> Tags { get; } = [];
}

public class Tag : TenantEntity<Guid>
{
    public int Id { get; set; }
    public List<Post> Posts { get; } = [];
}

public class PostTag : TenantEntity<Guid>
{
    public int PostId { get; set; }
    public int TagId { get; set; }
}

public class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Post>()
            .HasMany(p => p.Tags)
            .WithMany(t => t.Posts)
            .UsingEntity<PostTag>();
}
```

The join rows are then tenant-scoped rows like any other. `post.Tags.Add(tag)` inserts one stamped with the current
tenant, queries through `Tags` and `Posts` read only the current tenant's join rows, and a delete checks the stored
`TenantId`, so it matches no row of another tenant's. An existing join table needs a migration that adds the `TenantId`
column and fills it from the row at a tenant-scoped end.

## Saves that succeed or fail as a whole

Rows checked through another statement are safe only if the whole save is undone when the check fails. EF Core
usually ensures this with a transaction, but not in every setup. For these saves:

- The failed check cannot be suppressed. An interceptor of yours that suppresses concurrency failures
  (`ThrowingConcurrencyException`), such as "last write wins" or EF Core's sample that ignores rows already deleted,
  still works for other entities, but Tenantry throws a failed check that other rows depend on, wherever yours is
  registered. Interceptors added after `UseTenantry()`, including those packages add through it, do not see it.
- With `Database.AutoTransactionBehavior` set to `Never`, EF Core still runs the save in a transaction of its own, and
  the setting goes back to `Never` when the save ends (event 2005, at `Debug`). Other saves still run without one. If
  your database or connection pooler cannot run transactions, set `OnSaveWithoutTransaction` to `Reject`: such a save
  then throws `TenantIsolationViolationException` of kind `SaveWithoutTransaction` before anything is sent.
  - Hand a transaction you began through ADO.NET to EF Core with `Database.UseTransaction`, or EF Core cannot begin
    its own and the save fails.
  - If a `SavingChanges` interceptor registered after Tenantry's stops the save, the setting stays `WhenNeeded` until
    the context's next save sets it back to `Never`.
- In your own transaction, EF Core rolls a failed save back to a savepoint it creates first, and Tenantry turns
  savepoints on for the save if you turned them off (`AutoSavepointsEnabled = false`). A transaction without
  savepoints, such as SQL Server with multiple active result sets (MARS), is rolled back instead of committed if such a
  save in it failed after sending any statement, or if EF Core could not roll back to its savepoint. `Commit` then
  throws `TenantIsolationViolationException` of kind `TransactionRolledBack` (event 2004).
  - Any failure of such a save counts. A save can fail before EF Core reads the check (on a duplicate key, say), so
    Tenantry cannot know whether the check held, and a forged write looks like a real conflict.
  - So does a save Tenantry never learns succeeded, as when an interceptor added before `UseTenantry()` throws from
    `SavedChanges`.
- In a `TransactionScope`, or a transaction the connection was enlisted in, EF Core creates no savepoint. The same
  failures roll the transaction back when it completes, so disposing the completed scope throws
  `TransactionAbortedException`.

Not covered:

- EF Core's in-memory provider, which has no transactions;
- SQLite, or another provider that cannot join a `TransactionScope`, used inside one with EF Core's
  `AmbientTransactionWarning` turned off: it then saves with no transaction at all;
- storage without transactions, such as MySQL's MyISAM tables;
- an interceptor that suppresses EF Core's savepoint commands;
- a transaction handed to EF Core with `UseTransaction` and then committed directly through ADO.NET.

## Models that cannot be isolated

Building these models throws `TenantIsolationViolationException` (or `InvalidOperationException` for the registration):

- a tenant-scoped type whose base entity type is not tenant-scoped;
- a tenant-scoped owned type whose owner is not tenant-scoped;
- an owned type with no `TenantId`, under a tenant-scoped owner, whose key does not include its owner's key
  (`OwnsMany(…, b => b.HasKey(x => x.Id))`): an update or delete by that key could reach another tenant's row. Keep EF
  Core's default key, or implement `ITenantEntity<TKey>` on it;
- an owned type owned by a tenant-scoped type through a key that neither includes nor is part of the owner's primary
  key, nor includes its `TenantId` (`WithOwner().HasPrincipalKey(o => o.Code)`): Tenantry checks the owner by its
  primary key, which need not be the row the owned rows name;
- a many-to-many relationship with a tenant-scoped type at either end whose join entity is not tenant-scoped (see
  [Many-to-many relationships](#many-to-many-relationships));
- a tenant-scoped owned type mapped to JSON (`ToJson()`): it lives in its owner's row, under the owner's `TenantId`,
  and EF Core cannot check a `TenantId` of its own (EF Core 10 rejects the concurrency token itself), so do not
  implement `ITenantEntity<TKey>` on it;
- a type that is not tenant-scoped mapped to a tenant-scoped entity's table (table splitting): with no filter or
  `TenantId`, it would read and change every tenant's rows there. This fails on the first query or save, as only the
  finished model says which tables a type is mapped to;
- entities that implement `ITenantEntity<TKey>` with more than one key type;
- entities whose key type Tenantry is not registered for (`AddTenantry<Guid>` with `ITenantEntity<string>`);
- a tenant-scoped entity whose `TenantId` is not a mapped public property of the key type, such as one implemented
  explicitly (`Guid ITenantEntity<Guid>.TenantId => OrganizationId`);
- on EF Core 10, a filter of your own named `TenantryQueryFilters.Tenant`, which the tenant filter would replace.

Something that runs after `UseTenantry()`, such as a model-building convention, can still remove the tenant filter or
concurrency token. The interceptors check each model on its first query and first save, and throw
`TenantIsolationViolationException` instead of running either if a tenant-scoped entity type has lost one.
