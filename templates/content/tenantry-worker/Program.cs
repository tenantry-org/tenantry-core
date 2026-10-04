// A multi-tenant worker, from the tenantry-worker template (https://tenantry.dev/docs/core/ai-agents has the rules to
// keep). It takes messages that name their tenant by id, and runs each as that tenant with RunInScopeAsync, which
// looks the tenant up and refuses one that does not exist, so EF Core reads and writes only that tenant's rows.
//
// Run it: dotnet run. It processes the demonstration messages queued below, then waits for more.
// Replace WorkQueue with your message broker's consumer, and the in-memory store with one backed by your database.

using Microsoft.EntityFrameworkCore;
using Tenantry;
using TenantryWorker;

var builder = Host.CreateApplicationBuilder(args);

var tenants = builder.Configuration.GetSection("Tenants").Get<List<TenantDescriptor<string>>>() ?? [];

builder.Services.AddTenantry<string>(tenant => tenant.UseInMemoryStore(tenants)); // use a database-backed store in production

builder.Services.AddDbContext<OrdersDbContext>(options => options
    .UseSqlite(builder.Configuration.GetConnectionString("Orders"))
    .UseTenantry()); // filters queries and checks writes by the current tenant

builder.Services.AddSingleton<WorkQueue>();
builder.Services.AddHostedService<OrderWorker>();

var host = builder.Build();

// Create the schema on startup. Use EF Core migrations in production.
await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync();
}

// Demonstration messages: one for each tenant, and one for a tenant the store does not have.
var queue = host.Services.GetRequiredService<WorkQueue>();
await queue.EnqueueAsync(new OrderMessage("acme", "Acme's first order"));
await queue.EnqueueAsync(new OrderMessage("initech", "An order for a tenant that does not exist"));
await queue.EnqueueAsync(new OrderMessage("globex", "Globex's first order"));

await host.RunAsync();
