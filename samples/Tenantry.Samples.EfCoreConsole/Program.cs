// Tenantry EF Core Console Sample — multi-tenancy with NO ASP.NET Core.
//
// This sample shows how AddTenantryCore wires up the full tenant-isolation
// infrastructure for a non-HTTP host (console app, worker service, desktop UI, CLI…).
// There is no middleware and no request: YOU open and close the tenant scope manually
// with ITenantScope<TKey>.BeginScope(...) around the work that should run as a tenant.
//
// It demonstrates:
//   1. Registering core + EF Core isolation with AddTenantryCore (no AspNetCore package).
//   2. Stamping TenantId automatically on insert.
//   3. Reads being transparently filtered to the active tenant.
//   4. Nested scopes (an inner tenant shadows the outer one, restored on dispose).
//   5. Strict isolation catching a cross-tenant write before it hits the database.
//   6. Fail-closed behaviour when no tenant scope is active.
//   7. Bypassing isolation deliberately for admin/reporting with IgnoreQueryFilters().
//
// Run:
//   dotnet run --project samples/Tenantry.Samples.EfCoreConsole

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;
using Tenantry.Samples.EfCoreConsole;

// Two tenants we will switch between. In a real worker these would come from your
// ITenantStore (a database, config, message metadata, etc.).f
var acme = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Name = "Acme" };
var globex = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b2"), Name = "Globex" };

// Host.CreateApplicationBuilder gives us DI + logging + configuration without any web stack.
var builder = Host.CreateApplicationBuilder(args);

// Quieten the host's lifetime chatter so the sample's own output is easy to read,
// but keep Warning+ so Tenantry's strict-mode diagnostics are visible.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ── 1. Register the core Tenantry infrastructure (NOT AddTenantry — that's ASP.NET only) ──
builder.Services.AddTenantryCore<Guid>(tenant =>
{
    // A store is optional for AddTenantryCore (nothing resolves tenants for you off the
    // request like the ASP.NET middleware does), but registering one lets you look tenants
    // up by id from anywhere — e.g. when a queued message only carries the tenant id.
    tenant.UseInMemoryStore([acme, globex]);

    // Turn on EF Core write isolation. Cross-tenant Modified/Deleted writes are always rejected
    // DetectSpoofedWrites also rejects Added entities pre-stamped with a foreign tenant id.
    tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
});

// ── 2. Register the DbContext and attach the tenant interceptors ──────────────────────────
builder.Services.AddDbContext<SampleDbContext>((sp, options) =>
    options.UseSqlite("Data Source=tenantry-console-sample.db")
           .AddTenantInterceptors(sp));

using var host = builder.Build();

// A console app has no request scope, so create one DI scope for our unit of work.
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var tenantScope = sp.GetRequiredService<ITenantScope<Guid>>();
var db = sp.GetRequiredService<SampleDbContext>();

// Fresh database every run so the sample is reproducible.
await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

// ── 3. Do some work as Acme ───────────────────────────────────────────────────────────────
Order acmeOrder;
using (tenantScope.BeginScope(acme))
{
    db.Orders.Add(new Order { Description = "Acme widget order" });
    db.Orders.Add(new Order { Description = "Acme gadget order" });

    // We never set TenantId — the interceptor stamps it from the active scope.
    await db.SaveChangesAsync();

    acmeOrder = await db.Orders.FirstAsync();
    Print("Acme", $"sees {await db.Orders.CountAsync()} order(s); first TenantId = {acmeOrder.TenantId}");
}

// ── 4. Do some work as Globex (note the automatic read isolation) ──────────────────────────
using (tenantScope.BeginScope(globex))
{
    db.Orders.Add(new Order { Description = "Globex sprocket order" });
    await db.SaveChangesAsync();

    // The global query filter restricts this to Globex's rows only — Acme's are invisible.
    Print("Globex", $"sees {await db.Orders.CountAsync()} order(s) (Acme's are filtered out)");

    // ── 5. Strict isolation blocks a cross-tenant write ────────────────────────────────────
    // acmeOrder is still tracked by the context. Mutating it while Globex is active is a
    // cross-tenant modification; strict mode aborts SaveChanges before anything is written.
    try
    {
        acmeOrder.Description = "tampered by Globex";
        await db.SaveChangesAsync();
        Print("Globex", "ERROR: cross-tenant write was NOT blocked (unexpected)");
    }
    catch (TenantIsolationViolationException ex)
    {
        Print("Globex", $"blocked cross-tenant write: {ex.EntityTypeName} belongs to {ex.OffendingTenantId}");
        db.Entry(acmeOrder).State = EntityState.Unchanged; // discard the bad change
    }

    // ── 6. Nested scopes: temporarily act as Acme, then fall back to Globex ────────────────
    using (tenantScope.BeginScope(acme))
    {
        Print("Globex→Acme (nested)", $"sees {await db.Orders.CountAsync()} order(s)");
    }

    Print("Globex (restored)", $"sees {await db.Orders.CountAsync()} order(s) again");
}

// ── 7. No active scope = fail closed ───────────────────────────────────────────────────────
// With no tenant resolved, the filter matches nothing, so reads return zero rows rather than
// leaking every tenant's data. (A SaveChanges here would also log a missing-context warning.)
Print("No scope", $"sees {await db.Orders.CountAsync()} order(s) — isolation fails closed");

// ── 8. Admin / reporting: bypass isolation on purpose ───────────────────────────────────────
var total = await db.Orders.IgnoreQueryFilters().CountAsync();
Print("Admin", $"IgnoreQueryFilters() sees ALL {total} order(s) across every tenant");

return;

static void Print(string scopeName, string message) =>
    Console.WriteLine($"[{scopeName,-22}] {message}");
