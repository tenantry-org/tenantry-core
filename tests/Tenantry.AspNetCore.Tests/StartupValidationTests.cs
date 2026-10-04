using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;

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

        app.Invoking(a => a.UseTenantry())
            .Should().Throw<InvalidOperationException>().WithMessage("*no tenant resolvers*ResolveFromHeader*");
    }

    [Fact]
    public async Task UseTenantry_NoStoreRegistered_ThrowsClearError()
    {
        await using var app = Build(tenant => tenant.ResolveFromHeader("X-Tenant-Id"));

        app.Invoking(a => a.UseTenantry())
            .Should().Throw<InvalidOperationException>().WithMessage("*no tenant store*UseStore*UseInMemoryStore*");
    }

    [Fact]
    public async Task UseTenantry_WithoutTenantResolutionRegistered_SaysWhatToCall()
    {
        // AddTenantry without an ASP.NET Core feature, and without AddTenantry at all.
        await using var withCoreOnly = Build(tenant => tenant.UseInMemoryStore([]));
        await using var withNothing = Build(configure: null);

        foreach (var app in new[] { withCoreOnly, withNothing })
        {
            app.Invoking(a => a.UseTenantry())
                .Should().Throw<InvalidOperationException>().WithMessage("*found no tenant resolution*AddTenantry*ResolveFromHeader*");
        }
    }

    [Fact]
    public async Task UseTenantry_Registered_StartsAndStops()
    {
        await using var app = Build(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }]));

        app.UseTenantry();
        await app.StartAsync(TestContext.Current.CancellationToken);

        app.Lifetime.ApplicationStarted.IsCancellationRequested.Should().BeTrue();
        await app.StopAsync(TestContext.Current.CancellationToken);
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
