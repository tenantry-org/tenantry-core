using Microsoft.EntityFrameworkCore;
using Tenantry.Samples.EfCoreWeb.Entities;

namespace Tenantry.Samples.EfCoreWeb.Data;

public static class DataSeeder
{
    public static void Seed(DbContext db)
    {
        SeedTenants(db);
        SeedCatalog(db);
    }

    public static async Task SeedAsync(DbContext db)
    {
        await SeedTenantsAsync(db);
        await SeedCatalogAsync(db);
    }

    private static void SeedTenants(DbContext db)
    {
        var tenants = db.Set<Tenant>();

        if (tenants.Any())
        {
            return;
        }

        tenants.AddRange(Tenants);

        db.SaveChanges();
    }

    private static async Task SeedTenantsAsync(DbContext db)
    {
        var tenants = db.Set<Tenant>();

        if (await tenants.AnyAsync())
        {
            return;
        }

        tenants.AddRange(Tenants);

        await db.SaveChangesAsync();
    }

    private static void SeedCatalog(DbContext db)
    {
        var categories = db.Set<Category>();

        // Only seed if the database is empty
        if (categories.Any())
        {
            return;
        }

        // Seed global reference data (Categories and Products)
        // Every tenant shares this data
        categories.AddRange(Electronics, Office, Furniture);

        // Seed Products
        db.Set<Product>().AddRange(Products);

        db.SaveChanges();
    }

    private static async Task SeedCatalogAsync(DbContext db)
    {
        var categories = db.Set<Category>();

        // Only seed if the database is empty
        if (await categories.AnyAsync())
        {
            return;
        }

        // Seed global reference data (Categories and Products)
        // Every tenant shares this data
        categories.AddRange(Electronics, Office, Furniture);

        // Seed Products
        db.Set<Product>().AddRange(Products);

        await db.SaveChangesAsync();
    }

    private static readonly List<Tenant> Tenants =
    [
        new()
        {
            TenantId = "acme",
            Name = "Acme Corp",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            SubscriptionTier = "Pro"
        },
        new()
        {
            TenantId = "globex",
            Name = "Globex LLC",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            SubscriptionTier = "Free"
        }
    ];

    private static readonly Category Electronics = new()
    {
        Name = "Electronics",
        Description = "Electronic devices and accessories"
    };

    private static readonly Category Office = new()
    {
        Name = "Office Supplies",
        Description = "Office equipment and stationery"
    };

    private static readonly Category Furniture = new()
    {
        Name = "Furniture",
        Description = "Office and home furniture"
    };

    private static readonly List<Product> Products =
    [
        // Electronics
        new() {Name = "Laptop - Dell XPS 13", Price = 1299.99m, Category = Electronics},
        new() {Name = "Monitor - 27\" 4K", Price = 449.99m, Category = Electronics},
        new() {Name = "Wireless Mouse", Price = 29.99m, Category = Electronics},
        new() {Name = "Mechanical Keyboard", Price = 89.99m, Category = Electronics},
        new() {Name = "USB-C Hub", Price = 49.99m, Category = Electronics},

        // Office Supplies
        new() {Name = "Paper Ream (500 sheets)", Price = 8.99m, Category = Office},
        new() {Name = "Ballpoint Pens (12 pack)", Price = 5.99m, Category = Office},
        new() {Name = "Sticky Notes Set", Price = 12.99m, Category = Office},
        new() {Name = "Stapler Heavy Duty", Price = 15.99m, Category = Office},
        new() {Name = "File Folders (25 pack)", Price = 18.99m, Category = Office},

        // Furniture
        new() {Name = "Ergonomic Office Chair", Price = 399.99m, Category = Furniture},
        new() {Name = "Standing Desk", Price = 599.99m, Category = Furniture},
        new() {Name = "Bookshelf - 5 Tier", Price = 149.99m, Category = Furniture},
        new() {Name = "File Cabinet - 3 Drawer", Price = 199.99m, Category = Furniture}
    ];
}
