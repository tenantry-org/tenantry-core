// Registers Tenantry the way an application does and runs work in a tenant scope, then loads every
// Tenantry assembly the packages delivered (see PackageLoadCheck).
using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddTenantry<string>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }]);
    tenant.AddEfCoreIsolation();
});

await using var provider = services.BuildServiceProvider();
await provider.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync("acme", (scope, _) =>
{
    var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenant;
    Console.WriteLine($"Tenant in scope: {tenant?.Name}");
    return Task.CompletedTask;
});

PackageLoadCheck.Run();
