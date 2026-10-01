using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenantry.Tests.Shared;

namespace Tenantry.Core.Tests;

/// <summary>Every Tenantry.Core registration, resolved in a host that validates it (see <see cref="Conformance"/>).</summary>
public sealed class ConformanceTests
{
    [Fact]
    public async Task EveryCoreService_Resolves_AndTheHostStarts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureContainer(new DefaultServiceProviderFactory(Conformance.ProviderOptions));
        builder.Services.AddScoped<ScopedTenantStore.Session>();
        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.UseStore<ScopedTenantStore>();
            tenant.CacheTenants();
            tenant.UseConnectionStrings(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");
        });

        using var host = builder.Build();

        await host.Services.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync("acme", (scope, _) =>
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);
            scope.ServiceProvider.GetRequiredService<CurrentTenantConnectionString<string>>().Get()
                .Should().Be("Database=app_acme");
            return Task.CompletedTask;
        });
        await Conformance.StartAndStopAsync(host);
    }
}
