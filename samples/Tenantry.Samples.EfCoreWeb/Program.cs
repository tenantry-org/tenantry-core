// Tenantry EF Core Sample — Demonstrates realistic multi-tenant EF Core usage
//
// This sample shows:
// - Real EF Core migrations (not EnsureCreated)
// - Tenants stored as EF entities, looked up via EfCoreTenantStore
// - Mixed tenanted/non-tenanted entities (Orders are tenanted, Products are global)
// - Relationships across tenant boundaries (OrderItem → Product)
// - Seeding global reference data and tenants
// - Admin queries with IgnoreQueryFilters()
//
// Run:
//   dotnet ef database update
//   dotnet run
//
// Try:
//   # Get product catalogue (global data, visible to all tenants)
//   curl http://localhost:5000/products
//
//   # Create an order for Acme tenant
//   curl -H "X-Tenant-Id: acme" -X POST -H "Content-Type: application/json" \
//        -d '{"items":[{"productId":1,"quantity":2}]}' http://localhost:5000/orders
//
//   # List Acme's orders
//   curl -H "X-Tenant-Id: acme" http://localhost:5000/orders
//
//   # List Globex's orders (different tenant — empty, isolation working)
//   curl -H "X-Tenant-Id: globex" http://localhost:5000/orders
//
//   # Admin stats (crosses tenant boundaries with IgnoreQueryFilters)
//   curl http://localhost:5000/admin/stats

using Microsoft.EntityFrameworkCore;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.Samples.EfCoreWeb.Data;
using Tenantry.Samples.EfCoreWeb.Entities;
using Tenantry.Samples.EfCoreWeb.Requests;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Register Tenantry ─────────────────────────────────────────────────────
builder.Services.AddTenantry<string>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseStore<EfCoreTenantStore>();
    tenant.AddEfCoreIsolation(options =>
    {
        options.StrictIsolation = true;
    });
});

// ── 2. Register EF Core with tenant interceptors ─────────────────────────────────
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlite("DataSource=efcore-sample.db");
    options.UseSeeding((db, _) => DataSeeder.Seed(db));
    options.UseAsyncSeeding((db, _, _) => DataSeeder.SeedAsync(db));
    options.AddTenantInterceptors(sp);
});

var app = builder.Build();

// ── 3. Add the tenant resolution middleware ───────────────────────────────────
app.UseTenantry();

// ── 4. Migrate database ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ── 5. Endpoints ──────────────────────────────────────────────────────────────

// Get product catalog (global reference data, NOT tenanted)
app.MapGet("/products", async (AppDbContext db) =>
{
    var products = await db.Products
        .Include(p => p.Category)
        .Select(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            Category = p.Category.Name
        })
        .ToListAsync();

    return Results.Ok(products);
});

// Get product categories
app.MapGet("/categories", async (AppDbContext db) =>
{
    var categories = await db.Categories
        .Select(c => new { c.Id, c.Name, c.Description })
        .ToListAsync();

    return Results.Ok(categories);
});

// List current tenant's orders (automatically filtered by TenantId)
app.MapGet("/orders", async (AppDbContext db, ITenantContext<string> ctx) =>
{
    if (!ctx.HasTenant)
        return Results.BadRequest("No tenant resolved.");

    var orders = await db.Orders
        .Include(o => o.Items)
            .ThenInclude(i => i.Product)
        .Select(o => new
        {
            o.Id,
            o.OrderNumber,
            o.TenantId,
            o.CreatedAt,
            o.TotalAmount,
            o.Status,
            Items = o.Items.Select(i => new
            {
                i.Id,
                ProductName = i.Product.Name,
                i.Quantity,
                i.UnitPrice
            })
        })
        .ToListAsync();

    return Results.Ok(orders);
});

// Create an order for the current tenant (TenantId stamped automatically)
app.MapPost("/orders", async (AppDbContext db, ITenantContext<string> ctx, CreateOrderRequest request) =>
{
    if (!ctx.HasTenant)
        return Results.BadRequest("No tenant resolved.");

    // Validate all products exist
    var productIds = request.Items.Select(i => i.ProductId).ToList();
    var products = await db.Products
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    if (products.Count != productIds.Distinct().Count())
        return Results.BadRequest("One or more products not found.");

    // Create order
    var order = new Order
    {
        OrderNumber = $"ORD-{DateTime.UtcNow.Ticks}",
        CreatedAt = DateTime.UtcNow,
        Status = "Pending",
        Items = [.. request.Items.Select(item => new OrderItem
        {
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            UnitPrice = products[item.ProductId].Price
        })]
    };

    order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    return Results.Created($"/orders/{order.Id}", new
    {
        order.Id,
        order.OrderNumber,
        order.TenantId,
        order.TotalAmount,
        ItemCount = order.Items.Count
    });
});

// Admin endpoint: Get statistics across ALL tenants
// Demonstrates using IgnoreQueryFilters() to bypass tenant isolation
app.MapGet("/admin/stats", async (AppDbContext db) =>
{
    // WARNING: This query crosses tenant boundaries!
    // Use IgnoreQueryFilters() carefully, only in admin/reporting scenarios.
    var stats = await db.Orders
        .IgnoreQueryFilters()  // Bypass tenant filter to see all orders
        .GroupBy(o => o.TenantId)
        .Select(g => new
        {
            TenantId = g.Key,
            OrderCount = g.Count(),
            TotalRevenue = g.Sum(o => o.TotalAmount)
        })
        .ToListAsync();

    var totalOrders = await db.Orders.IgnoreQueryFilters().CountAsync();
    var totalRevenue = await db.Orders.IgnoreQueryFilters().SumAsync(o => o.TotalAmount);

    return Results.Ok(new
    {
        TotalOrders = totalOrders,
        TotalRevenue = totalRevenue,
        ByTenant = stats
    });
});

// Show current tenant info
app.MapGet("/me", (ITenantContext<string> ctx) =>
{
    if (!ctx.HasTenant)
        return Results.Json(new { Tenant = "None" });

    return Results.Json(new
    {
        TenantId = ctx.CurrentTenantId,
        TenantName = ctx.CurrentTenant!.Name
    });
});

Console.WriteLine("""
Tenantry EF Core Sample is running!

Tenants (seeded from database):
  Acme:   acme
  Globex: globex

Try these commands:
  curl http://localhost:5000/products
  curl -H "X-Tenant-Id: acme" http://localhost:5000/me
  curl -H "X-Tenant-Id: acme" http://localhost:5000/orders
  curl -H "X-Tenant-Id: acme" -X POST -H "Content-Type: application/json" \
       -d '{"items":[{"productId":1,"quantity":2}]}' http://localhost:5000/orders
  curl http://localhost:5000/admin/stats
""");

await app.RunAsync();
