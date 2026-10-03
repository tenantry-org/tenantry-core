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
            tenant.ResolveFromHost();
            tenant.ResolveFromPropagationHeader(_ => true);
            tenant.UseResolver<NoTenantResolver>();
            tenant.UseResolver(new NoTenantResolver());
            tenant.UseResolver(_ => new NoTenantResolver());
            tenant.UseStore<ScopedTenantStore>();
            tenant.CacheTenants();
            tenant.UseConnectionStrings(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");
            tenant.RequireTenantByDefault();
            tenant.ConfigureResolution(options =>
            {
                options.OnResolved = _ => Task.CompletedTask;
                options.OnRejected = _ => Task.CompletedTask;
            });
            tenant.ValidateTenantAccess((_, _) => true);
            tenant.ValidateTenantAccess((_, _, _) => ValueTask.FromResult(true));
            tenant.ValidateTenantAccess<ScopedValidator>();
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> tenant) => tenant.CurrentTenantId).RequireTenant();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);
        }

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        (await client.GetStringAsync("/tenant", TestContext.Current.CancellationToken)).Should().Be("acme");

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    // A validator with a scoped dependency, as one that reads a DbContext has.
    private sealed class ScopedValidator(ScopedTenantStore.Session session) : ITenantAccessValidator<string>
    {
        public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<string> tenant, CancellationToken cancellationToken) =>
            ValueTask.FromResult(session is not null);
    }

    private sealed class NoTenantResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }
}
