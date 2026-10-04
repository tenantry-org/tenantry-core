# Analyzers

`Tenantry.EfCore` and `Tenantry.AspNetCore` carry Roslyn analyzers, so an application that references them gets
warnings for the mistakes that leave tenant data unprotected, in `dotnet build`, Visual Studio and Rider, with no other
package to install. They add no runtime dependency: the compiler loads them, and they are not copied to the output.

| Rule | Package | Default | Reports |
|------|---------|---------|---------|
| [TNY1001](#tny1001) | Tenantry.EfCore | Warning | An entity with a `TenantId` that does not implement `ITenantEntity<TKey>` |
| [TNY1002](#tny1002) | Tenantry.EfCore | Warning | `IgnoreQueryFilters()` on a query of a tenant-owned entity |
| [TNY1003](#tny1003) | Tenantry.EfCore | Info | Raw SQL on `Database`, which is not isolated |
| [TNY2001](#tny2001) | Tenantry.AspNetCore | Warning | The tenant resolved from the request with no access validator |
| [TNY3001](#tny3001) | Tenantry.EfCore | Info | `MakeCurrent` or `CreateScope` given a descriptor built in the call |
| [TNY3002](#tny3002) | Tenantry.EfCore | Warning | Blocking on `RunInScopeAsync` |

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

A type a `DbContext` maps (as a `DbSet<T>` property or with `modelBuilder.Entity<T>()`) has a `TenantId` property but
does not implement `ITenantEntity<TKey>`. Tenantry filters and checks only the types that implement it, so every tenant
reads and writes all of this type's rows.

Implement `ITenantEntity<TKey>`, or derive from `TenantEntity<TKey>`. In Visual Studio and Rider, the fix "Implement
ITenantEntity<TKey>" adds the interface, with `TKey` the type of `TenantId`. If every tenant shares the type, mark it
`[SharedAcrossTenants]`, which also states it in the model
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).

It is not reported for a type marked `[SharedAcrossTenants]` (or derived from one), a type without a `TenantId`
property, a tenant descriptor (a type that implements `ITenantDescriptor<TKey>`, as a tenant store's entity does), or a
type named `Tenant`, whose `TenantId` is its key by EF Core's convention.

## TNY1002

`IgnoreQueryFilters()` on a query of a tenant-owned entity removes Tenantry's tenant filter, so the query reads every
tenant's rows, and an `ExecuteUpdate` or `ExecuteDelete` after it changes them.

If the query is meant to cross tenants (an admin report, maintenance), put it behind an authorization check of its own
and suppress the warning there with the reason. If it is meant to ignore another filter only, on EF Core 10 name that
filter, which keeps the tenant filter: `IgnoreQueryFilters(["SoftDelete"])`. A call that names filters is reported only
when a name among them is the tenant filter's, `TenantryQueryFilters.Tenant`.

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
an access validator anywhere (with those methods, or a type that implements `ITenantAccessValidator<TKey>`), or if the
`AddTenantry` lambda passes its builder, or the builder's `Services`, to other code. `ResolveFromClaim` and
`ResolveFromPropagationHeader` are not reported: a claim comes from the authenticated user, and the propagation header
from callers its predicate trusts.

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
blocking on it there, as on a desktop app's UI thread, can deadlock.

Await it ([Desktop apps](non-http-hosts.md#desktop-apps)).
