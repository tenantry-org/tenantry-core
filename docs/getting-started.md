# Getting started

This guide builds a multi-tenant ASP.NET Core app with EF Core data isolation, starting from an empty project. Without
ASP.NET Core, read the first two sections, then [Non-HTTP hosts](non-http-hosts.md).

To start from a generated project instead, run `dotnet new install Tenantry.Templates`, then
`dotnet new tenantry-api -n Orders.Api`. That creates an ASP.NET Core API with EF Core. It takes the tenant from the
`X-Tenant-Id` header and checks it against the caller's JWT claims. It targets `net10.0`, so it needs the .NET 10 SDK.

## 1. Install the packages

For an ASP.NET Core app backed by EF Core:

```bash
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore
```

`Tenantry.Core` comes with them. Reference it directly only for the core types in a project that has neither.

Tenantry is built for .NET 10, and still ships for .NET 8 and 9 ([Compatibility](compatibility.md)). Each build of
`Tenantry.EfCore` uses the EF Core major of its target framework (8.x, 9.x or 10.x).

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
// authentication and tenant access validation (see access-control.md and the SecureApi sample).
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
    .UseSqlServer(connectionString)
    .UseTenantry());
```

## 7. Add the middleware

```csharp
var app = builder.Build();

app.UseTenantry();   // resolves the tenant; place after UseAuthentication() if resolving from claims
```

`UseTenantry()` must run before any endpoint that needs a tenant
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)).

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

```bash
# Acme creates and lists an order
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" -X POST "…/orders?description=Widgets"
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" "…/orders"   # shows the order

# Globex sees none of Acme's data
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000002" "…/orders"   # empty

# Missing/unknown/invalid tenant
curl "…/orders"                                                          # 400 (RequireTenant)
curl -H "X-Tenant-Id: not-a-guid" "…/orders"                             # 404 (names no tenant)
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000099" "…/orders"  # 404 (not in store)
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
