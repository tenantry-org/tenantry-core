using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public async Task StartAsync_NoResolverRegistered_ThrowsClearError()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        var app = builder.Build();

        var act = () => app.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tenant resolvers were registered*");
        
        await app.DisposeAsync();       
    }

    [Fact]
    public async Task StopAsync_WhenAppStopped_CompletesSuccessfully()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        await app.StartAsync();
        await app.StopAsync();

        app.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_NoStoreRegistered_ThrowsClearError()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
        });

        var app = builder.Build();

        var act = () => app.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tenant store was registered*");
        
        await app.DisposeAsync();
    }
}
