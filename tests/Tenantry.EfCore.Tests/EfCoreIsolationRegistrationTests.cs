using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.Core.Extensions;
using Tenantry.Core.Internal;
using Tenantry.Core.Stores;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests;

public sealed class EfCoreIsolationRegistrationTests
{
    [Fact]
    public void AddEfCoreIsolation_Default_RegistersInterceptorOptionsAndValidator()
    {
        ServiceCollection services = new();
        ITenantBuilder<string> builder = new TestTenantBuilder<string>(services);

        builder.AddEfCoreIsolation();

        services.Should().Contain(sd => sd.ServiceType == typeof(TenantSaveChangesInterceptor<string>));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantInterceptorConfigurator));
        services.Should().Contain(sd => sd.ServiceType == typeof(StrictIsolationValidator<string>));
        services.Should().Contain(sd => sd.ServiceType == typeof(EfCoreIsolationOptions));
    }

    [Fact]
    public void AddEfCoreIsolation_Default_UsesWarnPolicyAndNoSpoofDetection()
    {
        ServiceCollection services = new();
        ITenantBuilder<string> builder = new TestTenantBuilder<string>(services);

        builder.AddEfCoreIsolation();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<EfCoreIsolationOptions>();

        options.OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
        options.DetectSpoofedWrites.Should().BeFalse();
    }

    [Fact]
    public void AddEfCoreIsolation_CapturesConfiguredPolicy()
    {
        ServiceCollection services = new();
        ITenantBuilder<string> builder = new TestTenantBuilder<string>(services);

        builder.AddEfCoreIsolation(options =>
        {
            options.OnMissingTenant = MissingTenantBehavior.Reject;
            options.DetectSpoofedWrites = true;
        });

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<EfCoreIsolationOptions>();

        options.OnMissingTenant.Should().Be(MissingTenantBehavior.Reject);
        options.DetectSpoofedWrites.Should().BeTrue();
    }

    [Fact]
    public void AddTenantInterceptors_WithEfCoreIsolationRegistered_AttachesInterceptorToOptions()
    {
        // Exercises TenantInterceptorConfigurator.AddInterceptors (resolves TenantSaveChangesInterceptor
        // from DI) and DbContextOptionsBuilderExtensions.AddTenantInterceptors (resolves the configurator).
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(builder => builder.AddEfCoreIsolation());

        using var sp = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder();
        var result = optionsBuilder.AddTenantInterceptors(sp);

        result.Should().BeSameAs(optionsBuilder);
    }

    /// <summary>Minimal ITenantBuilder implementation for unit tests.</summary>
    private sealed class TestTenantBuilder<TKey>(IServiceCollection services) : TenantBuilder<TKey>(services)
        where TKey : IEquatable<TKey>, IParsable<TKey>;

    [Fact]
    public void TenantBuilder_UseStoreFactory_RegistersFactoryDirectly()
    {
        ServiceCollection services = new();
        ITenantBuilder<string> builder = new TestTenantBuilder<string>(services);

        builder.UseStore(_ => new InMemoryTenantStore<string>([]));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ITenantStore<string>>();

        store.Should().NotBeNull();
    }
}
