// Tenantry EF Core console sample: multi-tenancy without ASP.NET Core.
//
// With no request to resolve a tenant from, this sample makes a tenant current with
// ITenantContextSetter<TKey>.MakeCurrent around the work that runs as it.
// MakeCurrent does not look the tenant up or check that it is active; work that starts from an id
// goes through ITenantScopeFactory.RunInScopeAsync, which does both.
//
// It demonstrates:
//   1. Registering Tenantry with AddTenantry (no AspNetCore package).
//   2. Isolating a DbContext with UseTenantry().
//   3. Stamping TenantId automatically on insert.
//   4. Reads filtered to the current tenant.
//   5. A write for another tenant refused before it is sent.
//   6. Nested scopes (an inner tenant shadows the outer one, restored on dispose).
//   7. Fail-closed behaviour when no tenant is current.
//   8. Bypassing isolation deliberately for admin/reporting with IgnoreQueryFilters().
//   9. A worker-style sweep: every tenant in its own DI scope, with ITenantScopeFactory.
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

// Two tenants, built by hand. A real worker reads them from its ITenantStore through ITenantLookup.
var acme = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Name = "Acme" };
var globex = new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b2"), Name = "Globex" };

// A host with DI, logging and configuration, but no web stack.
var builder = Host.CreateApplicationBuilder(args);

// Warnings and above only, so EF Core's log of every SQL command does not hide the sample's output.
// Tenantry logs the refused write in step 5 as an error, so it still shows.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ── 1. Register Tenantry (no ASP.NET Core package needed) ───────────────────────────────
builder.Services.AddTenantry<Guid>(tenant =>
{
    // AddTenantry needs no store here, as no middleware resolves tenants from a request. A store lets code
    // look a tenant up by id, for example when a queued message carries only the tenant id.
    tenant.UseInMemoryStore([acme, globex]);
});

// ── 2. Register the DbContext, isolated by tenant ──────────────────────────────────────────
// UseTenantry() filters every query to the current tenant and checks every write: cross-tenant
// writes are rejected, including a new entity pre-stamped with another tenant's id.
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseSqlite("Data Source=tenantry-console-sample.db")
           .UseTenantry());

using var host = builder.Build();

// A console app has no request scope, so create one DI scope for this unit of work.
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var tenantContext = sp.GetRequiredService<ITenantContextSetter<Guid>>();
var db = sp.GetRequiredService<SampleDbContext>();

// A new database each run, so every run prints the same output.
await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

// ── 3. Do some work as Acme ───────────────────────────────────────────────────────────────
Order acmeOrder;
using (tenantContext.MakeCurrent(acme))
{
    db.Orders.Add(new Order { Description = "Acme widget order" });
    db.Orders.Add(new Order { Description = "Acme gadget order" });

    // TenantId is not set here: the interceptor stamps it with the current tenant.
    await db.SaveChangesAsync();

    acmeOrder = await db.Orders.FirstAsync();
    Print("Acme", $"sees {await db.Orders.CountAsync()} order(s); first TenantId = {acmeOrder.TenantId}");
}

// ── 4. Do some work as Globex, whose reads see only its own rows ──────────────────────────
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

// ── 7. No tenant current: isolation fails closed ───────────────────────────────────────────
// With no tenant current, the filter matches nothing, so reads return no rows rather than every
// tenant's. A SaveChanges of tenant-owned entities here is rejected.
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
