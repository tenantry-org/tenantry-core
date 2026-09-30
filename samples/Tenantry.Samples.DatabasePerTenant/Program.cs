// Tenantry Database-per-Tenant Sample: each tenant's data lives in its own database.
//
// UseConnectionStrings says how to find a tenant's connection string, and AddDbContextPerTenantDatabase
// connects each context to the current tenant's database, here from a pool. Every tenant gets its own SQLite
// file; with SQL Server or PostgreSQL the delegate would return a different database (or server) per tenant in
// the same way.
//
// The entities still implement ITenantEntity, so rows are stamped and filtered by tenant as well. That second
// layer catches a connection string that points at the wrong database.
//
// Run:
//   dotnet run --project samples/Tenantry.Samples.DatabasePerTenant

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tenantry;
using Tenantry.Samples.DatabasePerTenant;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddTenantry<string>(tenant =>
{
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" },
    ]);

    // One database per tenant. In production, build the name from the tenant's properties or look the
    // string up (GetConnectionStringAsync) from a secrets store.
    tenant.UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Data Source=tenant-{t.TenantId}.db");

    // No connection string here: each context gets the current tenant's. It also uses UseTenantry().
    tenant.AddDbContextPerTenantDatabase<NotesDbContext>((_, options) => options.UseSqlite(), pooled: true);
});

using var host = builder.Build();

var scopes = host.Services.GetRequiredService<ITenantScopeFactory<string>>();
var tenants = await host.Services.GetRequiredService<ITenantStoreAccessor<string>>().GetAllTenantsAsync();

foreach (var tenant in tenants)
{
    await using var scope = scopes.CreateScope(tenant);
    var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();

    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    db.Notes.Add(new Note { Text = $"{tenant.Name}'s first note" });
    await db.SaveChangesAsync();

    Console.WriteLine($"[{tenant.Name,-6}] {db.Database.GetConnectionString(),-28} {await db.Notes.CountAsync()} note(s): " +
                      string.Join(", ", await db.Notes.Select(n => n.Text).ToListAsync()));
}

// With only an id, look the tenant up and run as it.
var globexNotes = await scopes.RunInScopeAsync("globex", (scope, ct) =>
    scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync(ct));
Console.WriteLine($"[Globex] RunInScopeAsync sees {globexNotes} note(s) in its own database");
