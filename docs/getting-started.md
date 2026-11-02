# Getting started

This guide builds a multi-tenant ASP.NET Core app with EF Core data isolation, starting from an empty project. Without
ASP.NET Core, read the first two sections, then [Non-HTTP hosts](non-http-hosts.md).

To start from a generated project instead, run `dotnet new install Tenantry.Templates`, then
`dotnet new tenantry-api -n Orders.Api`. That creates an ASP.NET Core API with EF Core. It takes the tenant from the
`X-Tenant-Id` header and checks it against the caller's JWT claims. It targets `net10.0`: it builds with the .NET 10
SDK or later and runs on the .NET 10 runtime.

## 1. Install the packages

Create an empty web project and add Tenantry's ASP.NET Core and EF Core packages. This guide stores its data in a
SQLite file, so it adds EF Core's SQLite provider too; use your own database's provider instead.

```bash
dotnet new web -n Orders
cd Orders
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

`Tenantry.Core` comes with them. Reference it directly only for the core types in a project that has neither. For
another database, add its EF Core provider in place of SQLite's ([Databases](compatibility.md#databases)).

Tenantry is built for .NET 10 and .NET 11, and still ships for .NET 8 and 9 ([Compatibility](compatibility.md)). Each
build of `Tenantry.EfCore` uses the EF Core major of its target framework (8.x, 9.x, 10.x or 11.x), so the provider must
be of that major too.

## 2. Choose your tenant key type

`TKey` is the type of your tenant ids: `Guid`, `int`, `long` and `string` all work
([Core concepts](core-concepts.md#the-tenant-key-tkey) has the constraints). Use the same type in your entities, store
and registration. This guide uses `Guid`.

## 3. Register Tenantry

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

var builder = WebApplication.CreateBuilder(args);

// Introductory setup: any caller can select any tenant with the header. For production, add
// authentication and tenant access validation (see access-control.md and the tenantry-api template).
builder.Services.AddTenantry<Guid>(tenant =>
{
    // (a) Resolution: how the tenant is identified on each request.
    tenant.ResolveFromHeader("X-Tenant-Id");

    // (b) Storage: which tenants exist. Replace with a DB-backed store in production.
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Acme" },
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Globex" },
    ]);
});
```

The build reports this registration as warning [TNY2001](analyzers.md#tny2001) until you add an access validator
([Validating tenant access](access-control.md#validating-tenant-access)).

The registration methods need no `using` directive. `using Tenantry;` is for types such as `TenantDescriptor<TKey>`.
Builder methods chain, so the lambda can also be
`tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore(tenants)`. `app.UseTenantry()` (step 7) throws at
startup without a resolver or a store ([Startup validation](aspnetcore-integration.md#startup-validation)).
[Registration](core-concepts.md#registration) has the rules.

## 4. Mark your tenant-owned entities

An entity is tenant-owned when it implements `ITenantEntity<TKey>`, as the base class `TenantEntity<TKey>` does:

```csharp
using Tenantry;

public class Order : TenantEntity<Guid>   // adds a `Guid TenantId { get; set; }` property
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}
```

Leave `TenantId` unset: Tenantry stamps it on insert. Entities that do not implement `ITenantEntity<TKey>` are shared
by all tenants ([`ITenantEntity`](core-concepts.md#itenantentitytkey)).

## 5. Keep your DbContext as it is

Your `DbContext` needs no base class, interface or Tenantry calls. Configure it as usual, in any order, including your
own query filters:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every query filters on TenantId, so lead your indexes with it.
        modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.Id });
    }
}
```

## 6. Register the DbContext with `UseTenantry()`

`UseTenantry()` isolates the context. After `OnModelCreating`, it adds the tenant query filter to every
`ITenantEntity<Guid>` entity. It also attaches the interceptor that stamps and checks `TenantId` on `SaveChanges`. It
works the same with `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory`.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlite("Data Source=app.db")
    .UseTenantry());
```

## 7. Add the middleware

```csharp
var app = builder.Build();

// Creates app.db and the Orders table on the first run. A real application applies migrations instead.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseTenantry();   // resolves the tenant; place after UseAuthentication() if resolving from claims
```

`UseTenantry()` must run before any endpoint that needs a tenant
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). `EnsureCreated` needs no tenant: it creates the
schema and reads no rows. [Migrations](efcore-integration.md#migrations) has the `dotnet ef` commands for a real
application.

## 8. Use the tenant in your endpoints

```csharp
app.MapGet("/orders", async (AppDbContext db) =>
        // No Where(o => o.TenantId == …) needed: the query filter adds it.
        await db.Orders.ToListAsync())
   .RequireTenant();

app.MapPost("/orders", async (AppDbContext db, string description) =>
{
    db.Orders.Add(new Order { Description = description }); // TenantId stamped on save
    await db.SaveChangesAsync();
    return Results.Created();
}).RequireTenant();

app.Run();
```

## 9. Try it

Start the app with `dotnet run --urls http://localhost:5000`, then, from another terminal:

```bash
# Acme creates and lists an order
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" -X POST "http://localhost:5000/orders?description=Widgets"
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" http://localhost:5000/orders   # shows the order

# Globex sees none of Acme's data
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000002" http://localhost:5000/orders   # empty

# Missing/unknown/invalid tenant
curl http://localhost:5000/orders                                                          # 400 (RequireTenant)
curl -H "X-Tenant-Id: not-a-guid" http://localhost:5000/orders                             # 404 (names no tenant)
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000099" http://localhost:5000/orders  # 404 (not in store)
```

The rejections have an empty body. Add `builder.Services.AddProblemDetails()` for `application/problem+json` bodies
([Status codes](aspnetcore-integration.md#status-codes)).

## Next steps

- Replace the in-memory store with a real one: [Tenant stores](tenant-stores.md).
- Resolve tenants from subdomains, routes or claims: [Tenant resolution](tenant-resolution.md).
- Restrict which users may use which tenants: [Access control](access-control.md).
- The isolation policy, admin queries and migrations: [EF Core integration](efcore-integration.md).
- Test that each tenant reads and writes only its own data: [Testing](testing.md).
- Build-time checks for common Tenantry mistakes: [Analyzers](analyzers.md).
