# For AI coding agents

This page is written for a coding agent adding Tenantry to an application, and for the developer who reviews what the
agent did. Follow the steps in order, then write the test in [Verify isolation](#verify-isolation) and check the list
of [mistakes](#mistakes-and-the-correct-form). The other guides have the detail behind each step.

## When to use Tenantry

Use it when one deployment of a .NET application serves several customers (tenants) whose data must be kept apart,
and the data is in EF Core: rows of a shared database tagged with a `TenantId`, or a database per tenant. It resolves
the tenant of each HTTP request, or lets background work run as a tenant, and EF Core then filters every query and
checks every save for that tenant.

Do not use it to separate users within one tenant: that is authorization. Without EF Core, Tenantry still resolves
the tenant and keeps caches and options per tenant, but nothing keeps your data apart.

## Add it to an ASP.NET Core app with EF Core

1. Add the packages: `dotnet add package Tenantry.AspNetCore` and `dotnet add package Tenantry.EfCore`.
2. Pick the tenant key type (`Guid`, `string`, `int` or `long`) and use it everywhere below.
3. Make each tenant-owned entity implement `ITenantEntity<TKey>`, or derive from `TenantEntity<TKey>`. Leave
   `TenantId` unset when you add a row: Tenantry stamps it on save. Entities that every tenant shares (reference
   data, a product catalogue) implement nothing.
4. Add `UseTenantry()` to the `DbContext` options, in a registration method of the application's own that
   `Program.cs` calls, so the isolation test can call the same one ([Verify isolation](#verify-isolation)). The
   context needs no base class and no other change.
5. Register Tenantry with a resolver, a store, and an access validator. A resolver that reads the request (a header,
   a route value, the query string, the host or subdomain) lets any caller name any tenant, so check the tenant
   against the authenticated user.
6. Add `app.UseTenantry()` after `app.UseAuthentication()`, and before the endpoints. Without
   `app.UseTenantResolution()`, put it after `app.UseAuthorization()` too, so an anonymous caller gets 401 rather than
   403, unless an authorization policy needs the tenant: then put it before `app.UseAuthorization()`. With
   `app.UseTenantResolution()` (authentication settings per tenant), `app.UseAuthorization()` always comes after
   `app.UseTenantry()`.

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class Invoice : TenantEntity<Guid>   // tenant-owned: filtered and stamped
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

public class Currency                       // shared by every tenant: neither filtered nor stamped
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Currency> Currencies => Set<Currency>();
}

public static class BillingRegistration
{
    // The one registration of the context, which Program.cs and the isolation test both call.
    public static IServiceCollection AddBillingDbContext(
        this IServiceCollection services, Action<DbContextOptionsBuilder> database) =>
        services.AddDbContext<BillingDbContext>(options =>
        {
            database(options);
            options.UseTenantry();
        });
}
```

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")             // the tenant the caller asks for
    .ValidateTenantAccessByClaim("tenant_id")     // 403 unless the caller's token lists it
    .RequireTenantByDefault()                     // 400 for an endpoint called without a tenant
    .UseStore<EfCoreTenantStore>());              // your ITenantStore<Guid>, which lists the tenants

builder.Services.AddBillingDbContext(options => options.UseSqlServer(connectionString));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTenantry();

app.MapGet("/invoices", (BillingDbContext db) => db.Invoices.ToListAsync());   // only the current tenant's rows
```

[Getting started](getting-started.md) has each step in full, [Access control](access-control.md) the validators, and
[Tenant stores](tenant-stores.md) how to write a store. The [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi)
is the shape to copy.

## Add it to a worker, console or desktop app

1. Add `Tenantry.EfCore` (it brings `Tenantry.Core`). Make entities tenant-owned and add `UseTenantry()` as above.
2. Register Tenantry with a store: `AddTenantry<TKey>(tenant => tenant.UseStore<T>())`.
3. Run each unit of work as a tenant with `ITenantScopeFactory<TKey>`. With an id from outside (a queue message, a
   command-line argument), use `RunInScopeAsync`, which looks the tenant up and refuses an unknown or inactive one:

```csharp
await scopes.RunInScopeAsync(message.TenantId, async (scope, ct) =>
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Orders.Add(new Order { Description = message.Description });
    await db.SaveChangesAsync(ct);
}, cancellationToken);
```

[Non-HTTP hosts](non-http-hosts.md#running-work-as-a-tenant) has the table that picks between `RunInScopeAsync`,
`CreateScope` and `MakeCurrent`. In short: an id from outside goes to `RunInScopeAsync`; a tenant you already loaded
from the store goes to `CreateScope`, or to `ITenantContextSetter<TKey>.MakeCurrent` when a scope already exists.

## Verify isolation

Write a test that runs the application's own registration of its context with Tenantry's real services and two
tenants: one tenant's rows must not be visible to the other, and a row for another tenant must be refused. The test
calls the same `AddBillingDbContext` that `Program.cs` calls, and replaces only the database, so it fails if the
application's registration loses `UseTenantry()`. A test that registers the context again with its own
`AddDbContext(... .UseTenantry())` passes whatever the application does, and proves nothing. This one runs on SQLite
in memory with xUnit:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore;
using Xunit;

public sealed class IsolationTests : IAsyncLifetime
{
    private static readonly TenantDescriptor<Guid> Acme = new() { TenantId = Guid.NewGuid(), Name = "Acme" };
    private static readonly TenantDescriptor<Guid> Globex = new() { TenantId = Guid.NewGuid(), Name = "Globex" };

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _services = new ServiceCollection()
            .AddTenantry<Guid>(tenant => tenant.UseInMemoryStore([Acme, Globex]))
            .AddBillingDbContext(options => options.UseSqlite(_connection))   // the application's own registration
            .BuildServiceProvider(validateScopes: true);

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EachTenantSeesOnlyItsOwnRows_AndAnotherTenantsRowIsRefused()
    {
        var scopes = _services.GetRequiredService<ITenantScopeFactory<Guid>>();

        await scopes.RunInScopeAsync(Acme.TenantId, async (scope, ct) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.Invoices.Add(new Invoice { Amount = 10 });
            await db.SaveChangesAsync(ct);
        });

        await scopes.RunInScopeAsync(Globex.TenantId, async (scope, ct) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            Assert.Empty(await db.Invoices.ToListAsync(ct));

            db.Invoices.Add(new Invoice { TenantId = Acme.TenantId, Amount = 20 });
            await Assert.ThrowsAsync<TenantIsolationViolationException>(() => db.SaveChangesAsync(ct));
        });
    }
}
```

Check that the test can fail: remove `options.UseTenantry()` from `AddBillingDbContext` and run it. It must fail;
put the call back. Tenantry's own tests run this test against a registration without `UseTenantry()` and check that it
fails. To check that every entity type is either tenant-owned or meant to be shared, assert that
`TenantModel.FindUnisolatedEntityTypes(db.Model)` lists only the types every tenant shares
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).
A request test through `WebApplicationFactory<Program>` adds what this test cannot see: how requests name a tenant,
that the access validator refuses a caller, and the pipeline order ([Testing](testing.md)).

## Mistakes and the correct form

`Tenantry.EfCore` and `Tenantry.AspNetCore` carry [analyzers](analyzers.md) that report most of these at build time
(TNY1001 to TNY3002). Fix what they report rather than suppress it, unless the code is meant to cross tenants.

- An entity with a `TenantId` property that does not implement `ITenantEntity<TKey>` is not tenant-owned: every
  tenant reads and writes all its rows. Implement `ITenantEntity<TKey>` (or derive from `TenantEntity<TKey>`).
- An entity type that implements nothing is shared by every tenant. That is the design, for reference data. Do not
  add filters or `TenantId` checks of your own to such types; make the type tenant-owned if its rows belong to one
  tenant. To have the model list every shared type, mark them `[SharedAcrossTenants]` and set `OnUnmarkedEntityType`
  ([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).
- A context registered without `UseTenantry()` isolates nothing: every tenant reads and writes all its tenant-owned
  rows, and nothing fails at run time. Call `UseTenantry()` in the options of every registration of a context with
  tenant-owned entities.
- `Database.SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlRaw` and `ExecuteSqlInterpolated` are not isolated: no
  filter applies and no check sees what they change. Prefer LINQ or `FromSql` on a tenant-owned set, which EF Core
  filters; otherwise add the tenant predicate yourself
  ([What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated)).
- `IgnoreQueryFilters()` removes the tenant filter from that whole query, so it reads every tenant's rows, its
  `Include`s and joins of tenant-owned entities too, and `ExecuteUpdate` or `ExecuteDelete` after it change them. Use
  it only in code meant to work across tenants, behind an authorization check of its own.
- Resolving the tenant from a header, route value, query string, host or subdomain with no access validator lets any
  caller act as any tenant. Add `ValidateTenantAccessByClaim(...)` or `ValidateTenantAccess(...)` in the same
  `AddTenantry`.
- `MakeCurrent` and `CreateScope` trust the descriptor they are given: they do not look it up or check that the tenant
  is active. Do not build a `TenantDescriptor` from an id that came from outside and pass it to them. Pass the id to
  `RunInScopeAsync`, or look the tenant up with `ITenantLookup<TKey>` first.
- A context from `AddDbContextPerTenantDatabase` is connected to one tenant's database. A query or save with it after
  the current tenant changes throws `TenantIsolationViolationException`. Resolve a new context in each tenant's scope.
- `RunInScopeAsync` returns to the caller's synchronization context to start the work, so blocking on it
  (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) on a desktop app's UI thread can deadlock. Await it
  ([Desktop apps](non-http-hosts.md#desktop-apps)).
- With `string` tenant ids, the database compares `TenantId` under the column's collation, and SQL Server's and MySQL's
  defaults ignore case: `acme` and `ACME` would read and change each other's rows. Use `Guid` ids, or ids the
  collation cannot confuse, or a binary collation on `TenantId`
  ([string tenant ids](efcore-integration.md#string-tenant-ids-and-the-databases-collation)).
  `UseInMemoryStore` refuses two ids that differ only in case.
- A tenant made current inside an `async` helper is not current for the helper's caller. Make it current, or open
  the scope, in the method that does the work ([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)).

## Rules for your AGENTS.md or CLAUDE.md

Copy this into the application's agent instructions:

```markdown
## Multi-tenancy (Tenantry)

- Tenant-owned entities implement `ITenantEntity<TKey>` (or derive from `TenantEntity<TKey>`). A `TenantId`
  property alone does nothing. Entities that implement neither are shared by every tenant on purpose.
- Never set `TenantId` on new rows and never filter by it in queries: `UseTenantry()` on the DbContext does both.
- Every `DbContext` that holds tenant-owned entities is registered with `.UseTenantry()`.
- `Database.SqlQuery`, `ExecuteSql*` and `IgnoreQueryFilters()` are not tenant-isolated. Do not use them for
  tenant data unless the code is meant to cross tenants and says so.
- Header, route, query-string, host and subdomain resolvers are always paired with an access validator in the same
  `AddTenantry`.
- Background work given a tenant id runs through `ITenantScopeFactory<TKey>.RunInScopeAsync(id, ...)`, and is
  awaited, never blocked on.
- `CreateScope` and `MakeCurrent` only take a tenant read from the store, never one built from outside input.
- Resolve DbContexts inside the tenant's scope; do not keep one across tenants.
- Prefer `Guid` tenant ids. String ids must not differ only in case or accents: SQL Server's and MySQL's default
  collations ignore case, and MySQL's also ignores accents.
- Every change to tenant-owned data access keeps the isolation test passing: one tenant cannot read or write
  another's rows.
```
