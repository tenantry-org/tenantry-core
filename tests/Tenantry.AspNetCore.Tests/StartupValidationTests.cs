using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// <c>app.UseTenantry()</c> checks the registration when the pipeline is built, so a misconfigured application fails
/// as it starts.
/// </summary>
public sealed class StartupValidationTests
{
    [Fact]
    public async Task UseTenantry_NoResolverRegistered_ThrowsClearError()
    {
        await using var app = Build(tenant => tenant
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }]));

        var act = () => app.UseTenantry();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no tenant resolvers*ResolveFromHeader*");
    }

    [Fact]
    public async Task UseTenantry_NoStoreRegistered_ThrowsClearError()
    {
        await using var app = Build(tenant => tenant.ResolveFromHeader("X-Tenant-Id"));

        var act = () => app.UseTenantry();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no tenant store*UseStore*UseInMemoryStore*");
    }

    [Fact]
    public async Task UseTenantry_WithoutTenantResolutionRegistered_SaysWhatToCall()
    {
        // AddTenantry without an ASP.NET Core feature, and without AddTenantry at all.
        await using var withCoreOnly = Build(tenant => tenant.UseInMemoryStore([]));
        await using var withNothing = Build(configure: null);

        foreach (var app in new[] { withCoreOnly, withNothing })
        {
            var act = () => app.UseTenantry();

            act.Should().Throw<InvalidOperationException>().WithMessage("*found no tenant resolution*AddTenantry*ResolveFromHeader*");
        }
    }

    [Fact]
    public async Task UseTenantry_Registered_StartsAndStops()
    {
        await using var app = Build(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }]));

        app.UseTenantry();
        await app.StartAsync();

        app.Lifetime.ApplicationStarted.IsCancellationRequested.Should().BeTrue();
        await app.StopAsync();
    }

    private static WebApplication Build(Action<ITenantBuilder<string>>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        if (configure is not null)
        {
            builder.Services.AddTenantry(configure);
        }

        return builder.Build();
    }
}
