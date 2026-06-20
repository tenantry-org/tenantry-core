using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Extensions;
using Tenantry.Core.Stores;

namespace Tenantry.Core.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddTenantryCore_WithNullConfigure_RegistersCoreServices()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>();

        using var sp = services.BuildServiceProvider();

        // Resolving these triggers the factory lambdas registered on lines 45-46
        var tenantContext = sp.GetRequiredService<ITenantContext<string>>();
        var tenantAccessor = sp.GetRequiredService<ITenantScope<string>>();

        tenantContext.Should().NotBeNull();
        tenantAccessor.Should().NotBeNull();
    }

    [Fact]
    public void AddTenantryCore_UseStoreFactory_RegistersFactoryStore()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>(builder =>
        {
            builder.UseStore(_ => new InMemoryTenantStore<string>([]));
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ITenantStore<string>>();

        store.Should().NotBeNull();
    }

    [Fact]
    public void AddTenantryCore_UseStoreGeneric_RegistersStoreType()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>(builder =>
        {
            builder.UseStore<StubTenantStore>();
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ITenantStore<string>>();

        store.Should().BeOfType<StubTenantStore>();
    }


    private sealed class StubTenantStore : ITenantStore<string>
    {
        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ITenantDescriptor<string>?>(null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([]);
    }

    // no additional internal-visibility tests here; internal TenantBuilder is exercised from the EfCore.Tests assembly
}
