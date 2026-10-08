# Analyzers

`Tenantry.EfCore` and `Tenantry.AspNetCore` carry Roslyn analyzers that warn, in `dotnet build`, Visual Studio and
Rider, of mistakes that leave tenant data unprotected. There is no other package to install, and no runtime dependency:
the compiler loads them, and they are not copied to the output.

| Rule | Package | Default | Reports |
|------|---------|---------|---------|
| [TNY1001](#tny1001) | Tenantry.EfCore | Warning | An entity with a `TenantId` that does not implement `ITenantEntity<TKey>` |
| [TNY1002](#tny1002) | Tenantry.EfCore | Warning | `IgnoreQueryFilters()` in a query that reads a tenant-owned entity |
| [TNY1003](#tny1003) | Tenantry.EfCore | Info | Raw SQL on `Database`, which is not isolated |
| [TNY1004](#tny1004) | Tenantry.EfCore | Warning | A context with tenant-owned entities registered without `UseTenantry()` |
| [TNY2001](#tny2001) | Tenantry.AspNetCore | Warning | The tenant resolved from the request with no access validator |
| [TNY3001](#tny3001) | Tenantry.EfCore | Info | `MakeCurrent` or `CreateScope` given a descriptor built in the call |
| [TNY3002](#tny3002) | Tenantry.EfCore | Info | Blocking on `RunInScopeAsync` |

The first digit of a rule's number is its area: 1 for EF Core isolation, 2 for resolution and access, 3 for tenant
scopes. The tenant scope rules are about `Tenantry.Core`'s API. They come with `Tenantry.EfCore`, which every
application that keeps tenant data in EF Core references.

Each rule reports what it can be sure of, and leaves alone most code it cannot see into. The sections on
[TNY1001](#tny1001), [TNY1004](#tny1004) and [TNY2001](#tny2001) name the cases they report anyway.

TNY1001, TNY1004 and TNY2001 decide once the whole project is compiled. `dotnet build` reports them, while Visual
Studio and Rider may show them only after a build or with analysis of the whole solution turned on. For the same reason
they have no code fix: the IDEs offer fixes only for diagnostics found file by file.

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
a warning fails the build. To silence one intended occurrence, give the reason next to it:

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
`TenantId`. A `TenantId` that cannot be a tenant key, such as `Guid?`, must first become one: a non-nullable `Guid`,
`int`, `long` or `string`. If every tenant shares the type, mark it shared, with `[SharedAcrossTenants]` or
`IsSharedAcrossTenants()`, which also states it in the model
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).

Tenant descriptors and registries:

- A tenant descriptor (a type that implements `ITenantDescriptor<TKey>`) is not reported.
- A type whose key is, or may be, its `TenantId`, as a tenant registry's is, is not reported. That is one with no
  other key by EF Core's conventions (an `Id` or `<Type>Id` property, a `[Key]`, or a `[PrimaryKey]` without
  `TenantId`).
- A registry with a key of its own and a `TenantId` column, in a context with tenant-owned types, is reported. Mark it
  `[SharedAcrossTenants]`.

It does not see a type mapped only in an `IEntityTypeConfiguration<T>`, or reached only through a navigation. A
marker called only through a delegate is not seen either, so mark those types `[SharedAcrossTenants]`
([what it counts](#what-tny1001-counts-as-mapped-and-marked)).

## TNY1002

`IgnoreQueryFilters()` removes Tenantry's tenant filter from the whole query. So a query that reads a tenant-owned
entity reads every tenant's rows of it, and an `ExecuteUpdate` or `ExecuteDelete` after it changes them.

The rule reports the call when the type the query reads is tenant-owned. It also reports it when the query brings a
tenant-owned type in: an `Include` or `ThenInclude` of it, a `Select`, `SelectMany`, `Join` or `GroupJoin` of it, or a
navigation to it or a query of it in one of the query's lambdas. The accidental case is usually a query of a shared
entity with a filter of its own, such as a soft delete:

```csharp no-compile
// Category is shared and has a soft-delete filter; Purchase is tenant-owned.
var categories = await db.Categories
    .IgnoreQueryFilters() // TNY1002: the included purchases are every tenant's
    .Include(c => c.Purchases)
    .ToListAsync();
```

- If the query is meant to cross tenants (an admin report, maintenance), put it behind an authorization check of its
  own, and suppress the warning there with the reason.
- If it is meant to ignore another filter only, on EF Core 10 name that filter, which keeps the tenant filter:
  `IgnoreQueryFilters(["SoftDelete"])`. A call that names filters is reported only when a name among them is the
  tenant filter's, `TenantryQueryFilters.Tenant`.
- On EF Core 8 and 9, which cannot name a filter, read the tenant-owned rows in a query of their own, without
  `IgnoreQueryFilters()`.

The rule looks at one expression ([Details](#what-tny1002-looks-at)). It does not see:

- a query composed across statements, kept in a local, or chosen with a conditional:
  `var q = db.Categories.IgnoreQueryFilters(); ... q.Include(c => c.Purchases)` is not reported, and neither is
  `(admin ? db.Categories.IgnoreQueryFilters() : db.Categories).Include(c => c.Purchases)`;
- a query returned from or passed to another method;
- a call in a subquery inside another query's lambda, which is checked against the subquery only;
- what runs in memory: after `AsEnumerable()`, and the selectors of methods that take a delegate rather than an
  expression, such as `ToDictionaryAsync`;
- the rest of an `Include` string path after a name it cannot find as a property, and an entity EF Core includes by
  itself (`AutoInclude()`).

## TNY1003

`Database.SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlRaw` and `ExecuteSqlInterpolated` (and their asynchronous
forms) map to no entity type. No tenant filter applies to their SQL, and no check sees what they change
([What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated)).

Use LINQ, or `FromSql` on a tenant-owned set, which EF Core filters. Otherwise add the tenant predicate yourself, with
the current tenant's id from `ITenantContext<TKey>`. The rule is info by default, since raw SQL is often deliberate.

## TNY1004

`AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` or `AddPooledDbContextFactory` registers a context that has
tenant-owned entities, and its options do not call `UseTenantry()`. Without it no tenant filter, `TenantId` stamping
or write check applies, so every tenant reads and changes every tenant's rows, and nothing fails or logs at run time.

```csharp no-compile
// TNY1004: AppDbContext has a DbSet<Order>, and Order implements ITenantEntity<Guid>.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
```

Add `.UseTenantry()` to the options. `AddDbContextPerTenantDatabase` applies `UseTenantry()` itself and is not
reported.

A context has tenant-owned entities when it, or a base context, has a `DbSet<T>` of one or maps one with
`modelBuilder.Entity<T>()` in its methods. A type mapped only in an `IEntityTypeConfiguration<T>`, or reached only
through a navigation, is not seen.

The rule reports a registration only when it can see everything the options do. It stays silent when:

- the options are not a lambda or a method of the project's own that cannot be overridden;
- the options store the builder in a field or property;
- the options pass the builder to code that could call `UseTenantry()`: a helper of the project's own that stores it
  or passes it on in the same way, a delegate, a method that can be overridden, another project of the solution, or a
  package that references `Tenantry.EfCore`;
- the context, or a base context, has an `OnConfiguring` that calls `UseTenantry()`, passes the builder on in the same
  way, or is in another assembly;
- another registration of the context, or a `ConfigureDbContext<TContext>`, in the same project calls `UseTenantry()`
  (on EF Core 8, not one later in the same method, as EF Core 8 uses only a context's first options);
- on EF Core 9 and later, the context is declared in another project, whose own registration may call `UseTenantry()`;
- the registration has no options, is in generated code, or is in a generic method whose context type the rule cannot
  tell.

It reports these cases anyway:

- A context declared in the same project, when only a library's `ConfigureDbContext<TContext>` helper applies
  `UseTenantry()`. Add `.UseTenantry()` to the registration too, which is harmless: `UseTenantry()` returns at once
  when the options already have it.
- On EF Core 8, a test project's second registration of the context. Add `.UseTenantry()` to it, or set
  `dotnet_diagnostic.TNY1004.severity = none` for the test project.
- Options that pass or store the builder inside an array, a tuple or a `params object[]`, through reflection, or as a
  method group to a framework or third-party method. Suppress the warning there.

Suppress it too where a context is meant to be unisolated, as in a test that registers one on purpose
([Configuring the rules](#configuring-the-rules)).

## TNY2001

`ResolveFromHeader`, `ResolveFromRouteValue`, `ResolveFromQueryString`, `ResolveFromHost` and `ResolveFromSubdomain`
read something the caller chooses, so with no access validator any caller can act as any tenant.

Add `ValidateTenantAccessByClaim(...)` or `ValidateTenantAccess(...)` in the same `AddTenantry`
([Validating tenant access](access-control.md#validating-tenant-access)). `ResolveFromClaim` and
`ResolveFromPropagationHeader` are not reported: a claim comes from the authenticated user, and the propagation header
from callers its predicate trusts.

The rule reports nothing when:

- the project adds an access validator anywhere with those methods;
- the project names `ITenantAccessValidator<TKey>` at all: a type that implements it, a registration such as
  `services.AddScoped<ITenantAccessValidator<Guid>, MembershipValidator>()`, or a `typeof`;
- the `AddTenantry` lambda passes its builder, or the builder's `Services`, to other code.

It does not see a validator registered only by a library's own extension method, in another assembly.

Where any caller may use any tenant on purpose, turn the rule off for that project or those files. Examples are a
public site per tenant with no signed-in users, where the host names the tenant whose pages are shown, and a test that
sends the header itself.

```ini
# A public site: the subdomain picks the tenant, and every visitor may see any tenant's pages.
[*.cs]
dotnet_diagnostic.TNY2001.severity = none
```

## TNY3001

`ITenantContextSetter<TKey>.MakeCurrent` or `ITenantScopeFactory<TKey>.CreateScope` is given a descriptor created in the
call (`new TenantDescriptor<TKey> { ... }`). Both trust the descriptor, without a store lookup or activity check
([Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant)). So a descriptor built from an id that came
from outside can name a tenant that does not exist or is suspended.

With an id, run the work with `RunInScopeAsync(tenantId, ...)`, which looks the tenant up and refuses a missing or
inactive one. Otherwise pass a tenant read from `ITenantLookup<TKey>`. A descriptor in a variable or field is not
reported. The rule is info by default, since tests build descriptors this way.

## TNY3002

`.Result`, `.Wait()` or `.GetAwaiter().GetResult()` on the task `RunInScopeAsync` returns, in the same expression. Await
it instead. `RunInScopeAsync` returns to the caller's synchronization context, so blocking on it there, as on a desktop
app's UI thread, can deadlock ([Desktop apps](non-http-hosts.md#desktop-apps)). The rule is info by default: ASP.NET
Core, workers and a console app's `Main` have no such context, so blocking there cannot deadlock.

## Details

### What TNY1001 counts as mapped and marked

- A context's types are those of its `DbSet<T>` properties, its base contexts' included, and of the
  `modelBuilder.Entity<T>()` calls in its methods. A type parameter constrained to a tenant-owned type counts as one.
- Generated code counts for its contexts, types and markers, and nothing is reported in it. Each type is reported
  once, where it is first mapped outside generated code.
- A context that maps no tenant-owned type is left alone. A database-per-tenant context, or one Tenantry does not
  isolate, has nothing to keep apart.
- A type is marked shared by `[SharedAcrossTenants]` on it or a base type, or by `IsSharedAcrossTenants()` anywhere in
  the project, in `OnModelCreating` or an `IEntityTypeConfiguration<T>`.
- A generic helper that marks its type parameter (`b.Entity<T>().IsSharedAcrossTenants()`) marks the type each call
  passes it, such as `Shared<Country>(b)`. That holds through helpers that pass their own type parameter on too. A
  helper nothing calls marks nothing.
- A marker whose type the rule cannot tell, such as the non-generic `IsSharedAcrossTenants()` in a loop over the
  model's types, silences the contexts that apply it. Those are the context whose `OnModelCreating` (or another of its
  methods) contains it, and the contexts that call the method containing it, create the configuration containing it,
  or call `ApplyConfigurationsFromAssembly`. The contexts derived from them are silenced too. Other contexts are still
  checked.
- A marker no context is seen to apply, such as dead code, silences nothing.

### What TNY1002 looks at

- The calls on the query `IgnoreQueryFilters()` is in, before and after it, through casts. Also the other queries
  passed to them: a `Join`'s or `GroupJoin`'s inner query, and the other query of a `Union`, `Concat`, `Intersect` or
  `Except`. All of them count, as EF Core ignores the filters for the whole query.
- A call that returns no query, such as `Count()` or `First()`, ends the chain. A value it computes passed as an
  argument, as in `Take(db.Orders.Count())`, is a query of its own.
- In each call, the type arguments of a call that returns a query, the navigations an `Include` string names, and the
  navigations and queries in the lambdas EF Core translates.
