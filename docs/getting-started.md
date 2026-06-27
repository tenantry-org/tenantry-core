# Getting started

This guide takes you from an empty project to a working multi-tenant ASP.NET Core app with EF Core
data isolation. If you are not using ASP.NET Core, read [Non-HTTP hosts](non-http-hosts.md) after
the first two sections.

## 1. Install the packages

For an ASP.NET Core app backed by EF Core:

```bash
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore
```

`Tenantry.Core` comes in transitively; reference it directly only if you want the core types in a
project that has neither of the above.

Tenantry targets **.NET 8, 9, and 10**. EF Core integration requires the matching major version of
`Microsoft.EntityFrameworkCore` (8.x, 9.x, or 10.x).

## 2. Choose your tenant key type

Every Tenantry API is generic over `TKey`, the type of your tenant identifier. `TKey` must implement
both `IEquatable<TKey>` (so EF Core can translate equality to SQL) and `IParsable<TKey>` (so resolvers
can parse the raw value out of a header, route, etc.).

`Guid`, `int`, `long`, and `string` all qualify out of the box. Pick one and use it consistently —
it appears in your entities, your `DbContext`, and your registration.

This guide uses `Guid`.

## 3. Register Tenantry

```csharp
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenantry<Guid>(tenant =>
{
    // (a) Resolution — how the tenant is identified on each request.
    tenant.ResolveFromHeader("X-Tenant-Id");

    // (b) Storage — which tenants exist. Replace with a DB-backed store in production.
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Acme" },
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Globex" },
    ]);

    // (c) Isolation — turn on EF Core write protection.
    tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
});
```

> **Startup validation.** `AddTenantry` registers a hosted service that fails fast at startup if you
> forgot to register a resolver or a store. You will get a clear `InvalidOperationException` rather
> than silent misbehaviour at runtime.

## 4. Mark your tenant-owned entities

An entity becomes tenant-scoped by implementing `ITenantScoped<TKey>`. The convenience base class
`TenantScoped<TKey>` implements it for you:

```csharp
using Tenantry.Core;

public class Order : TenantScoped<Guid>   // adds a `Guid TenantId { get; set; }` property
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}
```

Entities that do **not** implement `ITenantScoped<TKey>` are treated as global/shared data (product
catalogues, reference tables) and are never filtered or stamped. You never set `TenantId` yourself —
Tenantry stamps it on insert.

## 5. Make your DbContext tenant-aware

Your `DbContext` must expose the current tenant id so the query filters can read it. Implement
`ITenantAwareDbContext<TKey>` and call `ApplyTenantFilters` in `OnModelCreating`:

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore;
using Tenantry.EfCore.Extensions;

public class AppDbContext : DbContext, ITenantAwareDbContext<Guid>
{
    private readonly ITenantContext<Guid> _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext<Guid> tenantContext)
        : base(options) => _tenantContext = tenantContext;

    public DbSet<Order> Orders => Set<Order>();

    // EF Core re-reads this per query because it is a DbContext member — see Core concepts.
    public Guid? CurrentTenantId => _tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyTenantFilters<Guid, AppDbContext>(this); // filters every ITenantScoped<Guid> entity
    }
}
```

Prefer to start from a base class? Derive from `MultiTenantDbContext<Guid>` instead and the
`ITenantAwareDbContext` implementation plus `ApplyTenantFilters` call are done for you. See
[EF Core integration](efcore-integration.md#choosing-how-to-wire-the-dbcontext).

## 6. Register the DbContext with the interceptor

The interceptor is what stamps and validates `TenantId` on `SaveChanges`. Attach it from the
`AddDbContext` callback:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddTenantInterceptors(sp));   // requires AddEfCoreIsolation() above
```

## 7. Add the middleware

```csharp
var app = builder.Build();

app.UseTenantry();   // resolves the tenant; place after UseAuthentication() if resolving from claims
```

`UseTenantry()` must run **before** any endpoint that needs a tenant. With `WebApplication`, routing
is added automatically, so endpoint-level `RequireTenant()`/`AllowMissingTenant()` metadata is
respected. See [ASP.NET Core integration](aspnetcore-integration.md#pipeline-ordering) for ordering rules.

## 8. Use the tenant in your endpoints

```csharp
app.MapGet("/orders", async (AppDbContext db) =>
        // No Where(o => o.TenantId == …) needed — the global filter applies automatically.
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
curl -H "X-Tenant-Id: not-a-guid" "…/orders"                             # 400 (parse failure)
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000099" "…/orders"  # 404 (not in store)
```

## Next steps

- Replace the in-memory store with a real one — [Tenant stores](tenant-stores.md).
- Resolve tenants from subdomains, routes, or claims — [Tenant resolution](tenant-resolution.md).
- Restrict which users may access which tenants — [Access control](access-control.md).
- Understand the isolation policy, admin queries, and migrations — [EF Core integration](efcore-integration.md).
