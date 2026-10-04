// Tenantry EF Core console sample: multi-tenancy without ASP.NET Core.
//
// This sample shows how AddTenantry wires up the full tenant-isolation
// infrastructure for a non-HTTP host (console app, worker service, desktop UI, CLI…).
// There is no middleware and no request: YOU make a tenant current manually
// with ITenantContextSetter<TKey>.MakeCurrent(...) around the work that should run as a tenant.
// MakeCurrent and ITenantScopeFactory.CreateScope trust the descriptor they are given: they do not look it
// up in the store or check whether it is active. Work that starts from an id, such as a queue
// message, goes through ITenantScopeFactory.RunInScopeAsync, which does both.
//
// It demonstrates:
//   1. Registering Tenantry with AddTenantry (no AspNetCore package) and isolating a DbContext with UseTenantry().
//   2. Stamping TenantId automatically on insert.
//   3. Reads being transparently filtered to the active tenant.
//   4. Nested scopes (an inner tenant shadows the outer one, restored on dispose).
//   5. Write isolation catching a cross-tenant write before it hits the database.
//   6. Fail-closed behaviour when no tenant is current.
//   7. Bypassing isolation deliberately for admin/reporting with IgnoreQueryFilters().
//   8. A worker-style sweep: every tenant in its own DI scope, with ITenantScopeFactory.
//
// Run:
//   dotnet run --project samples/Tenantry.Samples.EfCoreConsole

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tenantry;
using Tenantry.EfCore;
using Tenantry.Samples.EfCoreConsole;

// Two tenants we will switch between. In a real worker these would come from your
// ITenantStore through ITenantLookup, not be built by hand.
var acme = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Name = "Acme" };
var globex = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b2"), Name = "Globex" };

// Host.CreateApplicationBuilder gives us DI + logging + configuration without any web stack.
var builder = Host.CreateApplicationBuilder(args);

// Quieten the host's lifetime chatter so the sample's own output is easy to read,
// but keep Warning+ so Tenantry's isolation diagnostics are visible.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ── 1. Register Tenantry (no ASP.NET Core package needed) ───────────────────────────────
builder.Services.AddTenantry<Guid>(tenant =>
{
    // A store is optional for AddTenantry (nothing resolves tenants for you off the
    // request like the ASP.NET middleware does), but registering one lets you look tenants
    // up by id from anywhere, for example when a queued message only carries the tenant id.
    tenant.UseInMemoryStore([acme, globex]);
});

// ── 2. Register the DbContext, isolated by tenant ──────────────────────────────────────────
// UseTenantry() filters every query to the current tenant and checks every write: cross-tenant
// writes are rejected, including a new entity pre-stamped with another tenant's id.
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseSqlite("Data Source=tenantry-console-sample.db")
           .UseTenantry());

using var host = builder.Build();

// A console app has no request scope, so create one DI scope for our unit of work.
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var tenantContext = sp.GetRequiredService<ITenantContextSetter<Guid>>();
var db = sp.GetRequiredService<SampleDbContext>();

// Fresh database every run so the sample is reproducible.
await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

// ── 3. Do some work as Acme ───────────────────────────────────────────────────────────────
Order acmeOrder;
using (tenantContext.MakeCurrent(acme))
{
    db.Orders.Add(new Order { Description = "Acme widget order" });
    db.Orders.Add(new Order { Description = "Acme gadget order" });

    // We never set TenantId: the interceptor stamps it from the active scope.
    await db.SaveChangesAsync();

    acmeOrder = await db.Orders.FirstAsync();
    Print("Acme", $"sees {await db.Orders.CountAsync()} order(s); first TenantId = {acmeOrder.TenantId}");
}

// ── 4. Do some work as Globex (note the automatic read isolation) ──────────────────────────
using (tenantContext.MakeCurrent(globex))
{
    db.Orders.Add(new Order { Description = "Globex sprocket order" });
    await db.SaveChangesAsync();

    // The global query filter restricts this to Globex's rows, so Acme's are not seen.
    Print("Globex", $"sees {await db.Orders.CountAsync()} order(s) (Acme's are filtered out)");

    // ── 5. Write isolation blocks a cross-tenant write ─────────────────────────────────────
    // acmeOrder is still tracked by the context. Mutating it while Globex is active is a
    // cross-tenant modification; Tenantry aborts SaveChanges before anything is written.
    try
    {
        acmeOrder.Description = "tampered by Globex";
        await db.SaveChangesAsync();
        Print("Globex", "ERROR: cross-tenant write was NOT blocked (unexpected)");
    }
    catch (TenantIsolationViolationException ex)
    {
        Print("Globex", $"blocked cross-tenant write: {ex.TypeName} belongs to {ex.OffendingTenantId}");
        db.Entry(acmeOrder).State = EntityState.Unchanged; // discard the bad change
    }

    // ── 6. Nested scopes: temporarily act as Acme, then fall back to Globex ────────────────
    using (tenantContext.MakeCurrent(acme))
    {
        Print("Globex→Acme (nested)", $"sees {await db.Orders.CountAsync()} order(s)");
    }

    Print("Globex (restored)", $"sees {await db.Orders.CountAsync()} order(s) again");
}

// ── 7. No active scope = fail closed ───────────────────────────────────────────────────────
// With no tenant resolved, the filter matches nothing, so reads return zero rows rather than
// leaking every tenant's data. A SaveChanges of tenant-owned entities here would be rejected.
Print("No scope", $"sees {await db.Orders.CountAsync()} order(s): isolation fails closed");

// ── 8. Admin / reporting: bypass isolation on purpose ───────────────────────────────────────
var total = await db.Orders.IgnoreQueryFilters().CountAsync();
Print("Admin", $"IgnoreQueryFilters() sees ALL {total} order(s) across every tenant");

// ── 9. A worker-style sweep over every tenant ───────────────────────────────────────────────
// Hosted services use ITenantScopeFactory: each scope is a fresh DI scope (so a fresh DbContext) with
// the tenant active, and disposing it restores "no tenant", so nothing carries over between tenants.
var scopes = host.Services.GetRequiredService<ITenantScopeFactory<Guid>>();
var tenants = host.Services.GetRequiredService<ITenantLookup<Guid>>();

foreach (var tenant in await tenants.GetAllTenantsAsync())
{
    await using var tenantWork = scopes.CreateScope(tenant);
    var tenantDb = tenantWork.ServiceProvider.GetRequiredService<SampleDbContext>();
    Print($"Sweep: {tenant.Name}", $"sees {await tenantDb.Orders.CountAsync()} order(s)");
}

Print("After sweep", $"tenant current: {tenantContext.HasTenant}");

return;

static void Print(string scopeName, string message) =>
    Console.WriteLine($"[{scopeName,-22}] {message}");
