# Owned and multi-table entities

Owned rows in a table of their own, the rows of an entity mapped to more than one table, and the join rows of a
many-to-many relationship have no tenant check of their own. Tenantry checks them through another statement and makes
the save all-or-nothing.

## Owned entities

EF Core reads owned rows only through their owner and allows them no filter of their own, so an owned type is isolated
through its owner whether or not it implements `ITenantEntity<TKey>`. A tenant-owned owned type's `TenantId` is still
a concurrency token.

Writes are checked through the nearest tenant-owned owner. A save that adds an owned entity, moves one with its own
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
  your filter hides, such as an archived one, can still be given owned entities.

An owned entity saved without its owner in the same context is rejected. With no tenant, `OnMissingTenant` treats
owned entities as tenant-owned. Owned rows in their own table rely on the owner's statement, so the save must succeed
or fail as a whole (below).

## Entities mapped to more than one table

An entity mapped to more than one table (table-per-type inheritance, entity splitting) is updated only in the tables
whose columns changed, so when one changes, its `TenantId` is also written back to its table to be checked there, or,
when EF Core does not save `TenantId`, its stored row is read before the save. One keyed by its `TenantId` needs
neither: every table's key names the tenant. A save that deletes such an entity and adds one under the same key, which
EF Core saves as an `UPDATE` of what differs, table by table, has the deleted one's stored row read. Rows outside the
table with `TenantId` rely on that table's statement, so the save must succeed or fail as a whole.

## Many-to-many relationships

A many-to-many relationship between tenant-owned types needs no join class: EF Core's own join entity works.

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

public class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts);
}
```

A join row holds the keys of the rows it joins and no `TenantId`. Reads through `Tags` and `Posts` return only the
current tenant's rows, as both ends' query filters apply. EF Core inserts and deletes join rows for ends that are
themselves unchanged, so a save that adds, changes or deletes a join row confirms each tenant-owned end it names, as
it confirms an owned row's owner:

- An end the save writes is checked by its own `UPDATE`, `DELETE` or `INSERT`.
- An end it does not write must have been loaded or attached as the current tenant, must still hold the current tenant's
  `TenantId` in memory, and has its stored row confirmed by writing its `TenantId` back with its concurrency token.
  Through a stub that carries another tenant's key with the current tenant's `TenantId`, that `UPDATE` matches no row,
  the save fails with `DbUpdateConcurrencyException`, and nothing is written. A stub that names another tenant fails
  with `TenantIsolationViolationException` before anything is sent.
- A join row saved without its tenant-owned ends tracked (added through the join entity's own set, with only key
  values) is refused with `TenantIsolationViolationException`.
- An end whose key in the join row includes its `TenantId` needs no confirmation: the join row can only name that
  tenant's row.

This costs one `UPDATE` for each end the save does not otherwise write, however many join rows name it: adding five
existing tags to an existing post sends six. New ends cost nothing, as their `INSERT` is the check. The confirmations
and the join rows succeed or fail together ([below](#saves-that-succeed-or-fail-as-a-whole)).

An end that is shared across tenants is not confirmed. A tenant's join row from its post to a shared tag is that
tenant's, and another tenant reading the tag's `Posts` sees none of it, as the posts' query filter applies through
the shared end. A join class of your own works the same way, payload columns included, and a change to a payload
column confirms the ends too. A join class that implements `ITenantEntity<TKey>` is checked by its own `TenantId`
instead, as any tenant-owned entity is. Deleting an end deletes its join rows: EF Core deletes those it tracks, which
the end's own `DELETE` checks, and the database's cascade deletes the others.

Not covered: `ExecuteUpdate` and `ExecuteDelete` on the join entity's set, and raw SQL against the join table, which
reach every tenant's join rows (the join entity has no query filter). Change join rows through the navigations.

## Saves that succeed or fail as a whole

Rows checked through another statement are safe only if the whole save is undone when the check fails. With EF Core's
defaults you need do nothing, and the same holds in a transaction you begin with `Database.BeginTransaction` that can
use savepoints: EF Core rolls a failed save back to a savepoint it creates first, and Tenantry turns savepoints on for
the save if you turned them off (`AutoSavepointsEnabled = false`). Other setups need care:

- With `Database.AutoTransactionBehavior` set to `Never`, EF Core still runs these saves in a transaction of its own,
  and the setting goes back to `Never` when the save ends (event 2005, at `Debug`). Other saves still run without one.
  Hand a transaction you began through ADO.NET to EF Core with `Database.UseTransaction`, or EF Core cannot begin its
  own and the save fails. If your database or connection pooler cannot run transactions, set `OnSaveWithoutTransaction`
  to `Reject`: such a save then throws `TenantIsolationViolationException` of kind `SaveWithoutTransaction` before
  anything is sent.
- In a transaction without savepoints, such as SQL Server with multiple active result sets (MARS), do not catch a
  failed save and carry on (after a unique or foreign key violation, or to retry a concurrency conflict). Once a save
  with such rows has run in it, the transaction is rolled back instead of committed if any save in it failed after
  sending a statement or EF Core could not roll back to its savepoint. `Commit` then throws
  `TenantIsolationViolationException` of kind `TransactionRolledBack` (event 2004). Turn MARS off to avoid it.
- In a `TransactionScope`, or a transaction the connection was enlisted in, EF Core creates no savepoint. The same
  failures roll the transaction back when it completes, so disposing the completed scope throws
  `TransactionAbortedException`. Begin the transaction with `Database.BeginTransaction` instead.

For these rollbacks, a failure counts even when an interceptor of yours suppresses it, as with a concurrency
conflict, or throws it from `SavedChanges` after the save succeeded. A save stopped before it sent anything does not
count. An interceptor of yours cannot suppress a failed tenant check in any setup: Tenantry throws it before any
interceptor added with `AddInterceptors` sees it. Such an interceptor still works for other entities.

Not covered:

- EF Core's in-memory provider, which has no transactions;
- SQLite, or another provider that cannot join a `TransactionScope`, used inside one with EF Core's
  `AmbientTransactionWarning` turned off: it then saves with no transaction at all;
- storage without transactions, such as MySQL's MyISAM tables;
- an interceptor that suppresses EF Core's savepoint commands;
- another EF Core options extension that inserts an interceptor at the start of the internal service provider's list
  after Tenantry's: it then runs before Tenantry's, and one that throws from a save's notices keeps them from
  Tenantry;
- a transaction handed to EF Core with `UseTransaction` and then committed directly through ADO.NET.

## Models that cannot be isolated

Building these models throws `TenantIsolationViolationException` (or `InvalidOperationException` for the registration):

- a tenant-owned type whose base entity type is not tenant-owned;
- a tenant-owned owned type whose owner is not tenant-owned;
- an owned type with no `TenantId`, under a tenant-owned owner, whose key does not include its owner's key
  (`OwnsMany(…, b => b.HasKey(x => x.Id))`): an update or delete by that key could reach another tenant's row. Keep EF
  Core's default key, or implement `ITenantEntity<TKey>` on it;
- an owned type owned by a tenant-owned type through a key that neither includes nor is part of the owner's primary
  key, nor includes its `TenantId` (`WithOwner().HasPrincipalKey(o => o.Code)`): Tenantry checks the owner by its
  primary key, which need not be the row the owned rows name;
- a many-to-many relationship whose join entity is not tenant-owned and has a key that does not include its foreign
  key to a tenant-owned end (`UsingEntity<TJoin>(…, j => j.HasKey(x => x.Id))`): an update or delete of a join row by
  that key could reach another tenant's row. Keep EF Core's default key, or implement `ITenantEntity<TKey>` on it;
- a many-to-many relationship whose join entity is not tenant-owned and names a tenant-owned end through a key that
  neither includes nor is part of the end's primary key, nor includes its `TenantId`
  (`HasForeignKey(…).HasPrincipalKey(e => e.Code)`): Tenantry confirms the end by its primary key, which need not be
  the row the join row names;
- a tenant-owned owned type mapped to JSON (`ToJson()`): it lives in its owner's row, under the owner's `TenantId`,
  and EF Core cannot check a `TenantId` of its own (EF Core 10 rejects the concurrency token itself), so do not
  implement `ITenantEntity<TKey>` on it;
- a type that is not tenant-owned mapped to a tenant-owned entity's table (table splitting): with no filter or
  `TenantId`, it would read and change every tenant's rows there. This fails on the first query or save, as only the
  finished model says which tables a type is mapped to;
- entities that implement `ITenantEntity<TKey>` with more than one key type;
- entities whose key type Tenantry is not registered for (`AddTenantry<Guid>` with `ITenantEntity<string>`);
- a tenant-owned entity whose `TenantId` is not a mapped public property of the key type, such as one implemented
  explicitly (`Guid ITenantEntity<Guid>.TenantId => OrganizationId`);
- on EF Core 10, a filter of your own named `TenantryQueryFilters.Tenant`, which the tenant filter would replace.

Something that runs after `UseTenantry()`, such as a model-building convention, can still remove the tenant filter or
concurrency token, so the interceptors check each model on its first query and first save, and throw
`TenantIsolationViolationException` instead of running either if a tenant-owned entity type has lost one.
