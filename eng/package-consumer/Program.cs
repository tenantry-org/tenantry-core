// Registers Tenantry the way an application does and runs work in a tenant scope, then loads every
// Tenantry assembly the packages delivered (see PackageLoadCheck).
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore;

var services = new ServiceCollection();
services.AddLogging();
services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }])
    .AddHttpPropagation()
    .IsolateCaches()
    .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject)
    .ConfigurePerTenant<ConsumerOptions>((options, t) => options.Name = t.Name));
services.AddHttpClient("service", client => client.BaseAddress = new Uri("https://service.internal")).UseTenantry();
_ = new DbContextOptionsBuilder().UseTenantry();

await using var provider = services.BuildServiceProvider();
await provider.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync("acme", (scope, _) =>
{
    var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenant;
    Console.WriteLine($"Tenant in scope: {tenant?.Name}");
    return Task.CompletedTask;
});

PackageLoadCheck.Run();

// Options configured per tenant.
internal sealed class ConsumerOptions
{
    public string Name { get; set; } = "";
}
