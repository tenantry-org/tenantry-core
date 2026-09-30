using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.AspNetCore;
using Tenantry.Tests.Shared;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Every Tenantry.AspNetCore registration, resolved in a web host that validates it (see <see cref="Conformance"/>),
/// which then serves a tenant request through the middleware.
/// </summary>
public sealed class ConformanceTests
{
    [Fact]
    public async Task EveryAspNetCoreService_Resolves_AndTheHostServesATenantRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = Conformance.ProviderOptions.ValidateScopes;
            options.ValidateOnBuild = Conformance.ProviderOptions.ValidateOnBuild;
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ScopedTenantStore.Session>();
        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.ResolveFromClaim();
            tenant.ResolveFromRouteValue();
            tenant.ResolveFromSubdomain();
            tenant.ResolveFromQueryString();
            tenant.UseResolver<NoTenantResolver>();
            tenant.UseResolver(new NoTenantResolver());
            tenant.UseResolver(_ => new NoTenantResolver());
            tenant.UseStore<ScopedTenantStore>();
            tenant.UseConnectionStrings(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");
            tenant.RequireTenantByDefault();
            tenant.ValidateTenantAccess((_, _) => true);
            tenant.ValidateTenantAccess((_, _, _) => ValueTask.FromResult(true));
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> tenant) => tenant.CurrentTenantId).RequireTenant();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        (await client.GetStringAsync("/tenant")).Should().Be("acme");

        await app.StopAsync();
    }

    private sealed class NoTenantResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }
}
