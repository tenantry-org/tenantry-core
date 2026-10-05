# Analyzers

`Tenantry.EfCore` and `Tenantry.AspNetCore` carry Roslyn analyzers, so an application that references them gets
warnings for the mistakes that leave tenant data unprotected, in `dotnet build`, Visual Studio and Rider, with no other
package to install. They add no runtime dependency: the compiler loads them, and they are not copied to the output.

| Rule | Package | Default | Reports |
|------|---------|---------|---------|
| [TNY1001](#tny1001) | Tenantry.EfCore | Warning | An entity with a `TenantId` that does not implement `ITenantEntity<TKey>` |
| [TNY1002](#tny1002) | Tenantry.EfCore | Warning | `IgnoreQueryFilters()` in a query that reads a tenant-owned entity |
| [TNY1003](#tny1003) | Tenantry.EfCore | Info | Raw SQL on `Database`, which is not isolated |
| [TNY2001](#tny2001) | Tenantry.AspNetCore | Warning | The tenant resolved from the request with no access validator |
| [TNY3001](#tny3001) | Tenantry.EfCore | Info | `MakeCurrent` or `CreateScope` given a descriptor built in the call |
| [TNY3002](#tny3002) | Tenantry.EfCore | Info | Blocking on `RunInScopeAsync` |

The first digit of a rule's number is its area: 1 for EF Core isolation, 2 for resolution and access, 3 for tenant
scopes. Each rule reports only what it can be sure of, so code it cannot see into is left alone. The tenant scope
rules are about `Tenantry.Core`'s API, and come with `Tenantry.EfCore`, which every application that keeps tenant data
in EF Core references.

## Configuring the rules

Set a rule's severity, for the whole project or for some files, in `.editorconfig`:

```ini
[*.cs]
# Fail the build on raw SQL, which the rule only notes by default.
dotnet_diagnostic.TNY1003.severity = warning

# The reporting project reads across tenants on purpose.
[src/Reporting/**.cs]
dotnet_diagnostic.TNY1002.severity = none
```

The severities are `error`, `warning`, `suggestion` (shown as info), `silent` and `none`. With `TreatWarningsAsErrors`,
a warning fails the build. To silence one occurrence that is meant, say why next to it:

```csharp no-compile
#pragma warning disable TNY1002 // the admin report counts every tenant's orders
var total = await db.Orders.IgnoreQueryFilters().CountAsync();
#pragma warning restore TNY1002
```

`[SuppressMessage("Tenantry", "TNY1002", Justification = "...")]` on a method does the same for the method.

## TNY1001

A type a `DbContext` maps has a `TenantId` property but does not implement `ITenantEntity<TKey>`, and the same context
maps tenant-owned types. Tenantry filters and checks only the types that implement it, so every tenant reads and
writes all of this type's rows.

Implement `ITenantEntity<TKey>`, or derive from `TenantEntity<TKey>`. The message names `TKey`, the type of
`TenantId`; a `TenantId` that cannot be a tenant key (`Guid?`, say) must first become one (a non-nullable `Guid`,
`int`, `long` or `string`). If every tenant shares the type, mark it shared, with `[SharedAcrossTenants]` or
`IsSharedAcrossTenants()`, which also states it in the model
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).

What it looks at:

- A context's types are those of its `DbSet<T>` properties, its base contexts' included, and of the
  `modelBuilder.Entity<T>()` calls in its methods. A type mapped only in an `IEntityTypeConfiguration<T>`, or reached
  only through a navigation, is not seen.
- A context that maps no tenant-owned type is left alone: a database-per-tenant context, or one Tenantry does not
  isolate, has nothing to keep apart.
- A type marked shared is not reported: `[SharedAcrossTenants]` on it or a base type, or `IsSharedAcrossTenants()`
  anywhere in the project, in `OnModelCreating` or an `IEntityTypeConfiguration<T>`. A generic helper that marks its
  type parameter (`b.Entity<T>().IsSharedAcrossTenants()`) marks the type each call passes it, such as
  `Shared<Country>(b)`, also through helpers that pass their own type parameter on. A helper nothing calls marks
  nothing.
- A marker whose type the rule cannot tell, such as the non-generic `IsSharedAcrossTenants()` in a loop over the
  model's types, silences the contexts that apply it: the context whose `OnModelCreating` (or another of its methods)
  contains it, the contexts that call the method containing it or create the configuration containing it, or call
  `ApplyConfigurationsFromAssembly`, and the contexts derived from them. Other contexts are still checked. A marker no
  context is seen to apply, such as dead code, silences nothing; one called only through a delegate is not seen
  either, so mark those types `[SharedAcrossTenants]`.
- A tenant descriptor (a type that implements `ITenantDescriptor<TKey>`) is not reported, and neither is a type whose
  key is, or may be, its `TenantId`, as a tenant registry's is: one with no other key by EF Core's conventions (an `Id`
  or `<Type>Id` property, a `[Key]`, or a `[PrimaryKey]` without `TenantId`). A registry with a key of its own and a
  `TenantId` column, in a context with tenant-owned types, is reported: mark it `[SharedAcrossTenants]`.

Each type is reported once, where it is first mapped. The rule decides once the whole project is compiled, so `dotnet
build` reports it, while an IDE may show it only after a build or with analysis of the whole solution turned on. For
the same reason it has no code fix: Visual Studio and Rider offer fixes only for diagnostics found file by file.

## TNY1002

`IgnoreQueryFilters()` removes Tenantry's tenant filter from the whole query, so a query that reads a tenant-owned
entity reads every tenant's rows of it, and an `ExecuteUpdate` or `ExecuteDelete` after it changes them. The rule reports
the call when the type the query reads is tenant-owned, and when the query brings a tenant-owned type in: an `Include`
or `ThenInclude` of it, a `Select`, `SelectMany`, `Join` or `GroupJoin` of it, or a navigation to it or a query of it in
one of the query's lambdas. A query of a shared entity with a filter of its own, such as a soft delete, is where this
happens by accident:

```csharp no-compile
// Category is shared and has a soft-delete filter; Purchase is tenant-owned.
var categories = await db.Categories
    .IgnoreQueryFilters() // TNY1002: the included purchases are every tenant's
    .Include(c => c.Purchases)
    .ToListAsync();
```

If the query is meant to cross tenants (an admin report, maintenance), put it behind an authorization check of its own
and suppress the warning there with the reason. If it is meant to ignore another filter only, on EF Core 10 name that
filter, which keeps the tenant filter: `IgnoreQueryFilters(["SoftDelete"])`. A call that names filters is reported only
when a name among them is the tenant filter's, `TenantryQueryFilters.Tenant`. On EF Core 8 and 9, which cannot name a
filter, read the tenant-owned rows in a query of their own, without `IgnoreQueryFilters()`.

The rule sees the calls on the query in the expression that calls `IgnoreQueryFilters()`, before and after it, up to the
first that runs in memory (`AsEnumerable()`, `ToDictionaryAsync`'s selectors). EF Core ignores the filters for the whole
query wherever the call is, so a `Join` that takes the query as its inner query counts too. A query kept in a variable
or returned from a method, and extended elsewhere, is seen only as far as that expression goes, and a call in a
subquery, inside another query's lambda, is checked against the subquery only. An `Include` path in a string is followed
as far as its navigations are properties of the types it names, and an entity EF Core includes by itself
(`AutoInclude()`) is not seen.

## TNY1003

`Database.SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlRaw` and `ExecuteSqlInterpolated` (and their asynchronous
forms) map to no entity type, so no tenant filter applies to their SQL and no check sees what they change.

Use LINQ, or `FromSql` on a tenant-owned set, which EF Core filters. Otherwise add the tenant predicate yourself, with
the current tenant's id from `ITenantContext<TKey>`. It is info by default, since raw SQL is often deliberate; see
[What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated).

## TNY2001

`ResolveFromHeader`, `ResolveFromRouteValue`, `ResolveFromQueryString`, `ResolveFromHost` and `ResolveFromSubdomain`
read something the caller chooses, so with no access validator any caller can act as any tenant.

Add `ValidateTenantAccessByClaim(...)` or `ValidateTenantAccess(...)` in the same `AddTenantry`
([Validating tenant access](access-control.md#validating-tenant-access)). The rule reports nothing if the project adds
an access validator anywhere with those methods, or names `ITenantAccessValidator<TKey>` at all (a type that implements
it, a registration such as `services.AddScoped<ITenantAccessValidator<Guid>, MembershipValidator>()`, a `typeof`),
or if the `AddTenantry` lambda passes its builder, or the builder's `Services`, to other code. A validator registered
only by a library's own extension method, in another assembly, is not seen. `ResolveFromClaim` and
`ResolveFromPropagationHeader` are not reported: a claim comes from the authenticated user, and the propagation header
from callers its predicate trusts.

Where any caller may use any tenant on purpose, turn the rule off for that project or those files: a public site per
tenant with no signed-in users, where the host names the tenant whose pages are shown, or a test that sends the header
itself.

```ini
# A public site: the subdomain picks the tenant, and every visitor may see any tenant's pages.
[*.cs]
dotnet_diagnostic.TNY2001.severity = none
```

It decides once the whole project is compiled, so `dotnet build` reports it, while an IDE may show it only after a
build, or with analysis of the whole solution turned on.

## TNY3001

`ITenantContextSetter<TKey>.MakeCurrent` or `ITenantScopeFactory<TKey>.CreateScope` is given a descriptor created in the
call (`new TenantDescriptor<TKey> { ... }`). Both trust the descriptor: they do not look it up in the store or check
that the tenant is active, so a descriptor built from an id that came from outside can name a tenant that does not exist
or is suspended.

With an id, run the work with `RunInScopeAsync(tenantId, ...)`, which looks the tenant up and refuses a missing or
inactive one; otherwise pass a tenant read from `ITenantLookup<TKey>`
([Non-HTTP hosts](non-http-hosts.md#running-work-as-a-tenant)). A descriptor in a variable or field is not reported. It
is info by default, since tests build descriptors this way.

## TNY3002

`.Result`, `.Wait()` or `.GetAwaiter().GetResult()` on the task `RunInScopeAsync` returns, in the same expression.
`RunInScopeAsync` returns to the caller's synchronization context to start the work and dispose the scope's services, so
blocking on it there, as on a desktop app's UI thread, can deadlock. It is info by default: ASP.NET Core, workers and a
console app's `Main` have no such context, and blocking there cannot deadlock.

Await it ([Desktop apps](non-http-hosts.md#desktop-apps)).
