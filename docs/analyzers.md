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
scopes. Each rule reports what it can be sure of and leaves alone most code it cannot see into; the sections on
[TNY1001](#tny1001), [TNY1004](#tny1004) and [TNY2001](#tny2001) name the cases they report anyway. The tenant scope
rules are about `Tenantry.Core`'s API, and come with `Tenantry.EfCore`, which every application that keeps tenant data
in EF Core references.

TNY1001, TNY1004 and TNY2001 decide once the whole project is compiled, so `dotnet build` reports them, while Visual
Studio and Rider may show them only after a build or with analysis of the whole solution turned on. For the same
reason they have no code fix: the IDEs offer fixes only for diagnostics found file by file.

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
`TenantId`; a `TenantId` that cannot be a tenant key, such as `Guid?`, must first become one (a non-nullable `Guid`,
`int`, `long` or `string`). If every tenant shares the type, mark it shared, with `[SharedAcrossTenants]` or
`IsSharedAcrossTenants()`, which also states it in the model
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).

What it looks at:

- A context's types are those of its `DbSet<T>` properties, its base contexts' included, and of the
  `modelBuilder.Entity<T>()` calls in its methods; a type parameter constrained to a tenant-owned type counts as one. A
  type mapped only in an `IEntityTypeConfiguration<T>`, or reached only through a navigation, is not seen. Generated
  code counts for its contexts, types and markers, and nothing is reported in it.
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

Each type is reported once, where it is first mapped outside generated code.

## TNY1002

`IgnoreQueryFilters()` removes Tenantry's tenant filter from the whole query, so a query that reads a tenant-owned
entity reads every tenant's rows of it, and an `ExecuteUpdate` or `ExecuteDelete` after it changes them. The rule
reports the call when the type the query reads is tenant-owned, and when the query brings a tenant-owned type in: an
`Include` or `ThenInclude` of it, a `Select`, `SelectMany`, `Join` or `GroupJoin` of it, or a navigation to it or a
query of it in one of the query's lambdas. The accidental case is usually a query of a shared entity with a filter of
its own, such as a soft delete:

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

The rule looks at one expression: the calls on the query `IgnoreQueryFilters()` is in, before and after it, and the
other queries passed to them (a `Join`'s or `GroupJoin`'s inner query, the other query of a `Union`, `Concat`,
`Intersect` or `Except`), through casts; all of them count, as EF Core ignores the filters for the whole query. A call
that returns no query, such as `Count()` or `First()`, ends the chain, and a value it computes passed as an argument, as
in `Take(db.Orders.Count())`, is a query of its own. In each call it looks at the type arguments of a call that returns
a query, at the navigations an `Include` string names, and at the navigations and queries in the lambdas EF Core
translates.

What it does not see:

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
forms) map to no entity type, so no tenant filter applies to their SQL and no check sees what they change.

Use LINQ, or `FromSql` on a tenant-owned set, which EF Core filters. Otherwise add the tenant predicate yourself, with
the current tenant's id from `ITenantContext<TKey>`. It is info by default, since raw SQL is often deliberate; see
[What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated).

## TNY1004

`AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` or `AddPooledDbContextFactory` registers a context that has
tenant-owned entities, and its options do not call `UseTenantry()`, which installs the tenant filter, the `TenantId`
stamping and the write checks. Without it every tenant reads and changes every tenant's rows, and nothing fails or logs
at run time.

```csharp no-compile
// TNY1004: AppDbContext has a DbSet<Order>, and Order implements ITenantEntity<Guid>.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
```

Add `.UseTenantry()` to the options. `AddDbContextPerTenantDatabase` applies `UseTenantry()` itself and is not
reported.

A context has tenant-owned entities when it, or a base context, has a `DbSet<T>` of one or maps one with
`modelBuilder.Entity<T>()` in its methods (a type parameter constrained to a tenant-owned type counts). Not seen: a type
mapped only in an `IEntityTypeConfiguration<T>` (or `ApplyConfigurationsFromAssembly`), with `Entity(typeof(T))` or with
`Entity<T>()` in another class, or reached only through a navigation, and a generic base context that maps an
unconstrained type parameter.

The rule reports a registration only when it sees everything the options do, so these are not reported:

- options that hand the builder to code that could call `UseTenantry()`, or store it in a field or property: a method
  of the project's own that does so in turn (a C# 14 extension method called on the builder too), another project of
  the solution, a package that references `Tenantry.EfCore` (directly or through another package), a delegate, an
  interface, virtual or unsealed override method, a local function, a constructor of the project's own, or a C# 14
  extension member called in static form (`Isolation.WithTenantry(options)`). A property read on the builder, of the
  project's own, of another project of the solution, or of a package that references `Tenantry.EfCore`, counts too: an
  extension property, or a property of a builder subclass. A method of the project's own that does none of this does not
  count, and neither does the framework;
- options that are not a lambda or a method of the project's own (a delegate in a variable), a method that can be
  overridden (a virtual, abstract or interface method), and a registration without options;
- a registration made through a generic method of the project's own (`AddDbContext<TContext>` inside a method generic
  in `TContext`), whose context type the rule cannot tell;
- a registration in generated code, where nothing is reported; generated code still counts for the helpers and
  `OnConfiguring` it holds;
- a context with an `OnConfiguring`, of its own or a base context's, that calls `UseTenantry()` or hands the builder
  on as above, or that is in another assembly. One that only picks a provider or adds logging, as a scaffolded
  context's does, does not count;
- a context that another registration, or a `ConfigureDbContext<TContext>`, in the same project calls `UseTenantry()`
  for, since from EF Core 9 they add to the same options. A generic method that does so for its type parameter counts
  for every context that meets the type parameter's constraints, every context when it is constrained only to
  `DbContext`. On EF Core 8, only a context's first registration's options apply, so another registration counts only
  when it is not later in the same method; a lambda's or local function's body is a method of its own here;
- on EF Core 9 and later, a context declared in another project, since a registration or `ConfigureDbContext<TContext>`
  in that project, which the rule cannot see, may have applied `UseTenantry()`.

A context declared in the same project is still reported when only a library's `ConfigureDbContext<TContext>` helper
applies `UseTenantry()`, as the rule cannot see into it: suppress the warning there, or add `.UseTenantry()` to the
registration, which is harmless, as `UseTenantry()` returns at once when the options already have it. On EF Core 8, a
test project's second registration of the context is reported too. If the test removes the application's options
(`services.RemoveAll<DbContextOptions<AppDbContext>>()`), the context gets only the test registration's options;
without the removal, EF Core 8 keeps the application's. Add `.UseTenantry()` to the test registration, or set
`dotnet_diagnostic.TNY1004.severity = none` for the test project.

The registrations in a method are all taken to run, in source order, so one on a branch that excludes another, after an
early return, or in a callback that runs later still counts. A few false reports remain by design: the builder passed or
stored inside an array, a tuple or a `params object[]`, through reflection, or as a method group to a framework or
third-party method, and a package that does not reference `Tenantry.EfCore` calling back into the application's override
of its virtual method. Suppress the warning there, as where a context is meant to be unisolated, such as in a test that
registers one on purpose ([Configuring the rules](#configuring-the-rules)).

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

Await it instead ([Desktop apps](non-http-hosts.md#desktop-apps)).
