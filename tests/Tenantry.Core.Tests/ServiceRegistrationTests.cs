using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddTenantry_WithNullConfigure_RegistersCoreServices()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();

        using var sp = services.BuildServiceProvider();

        var tenantContext = sp.GetRequiredService<ITenantContext<string>>();
        var setter = sp.GetRequiredService<ITenantContextSetter<string>>();

        tenantContext.Should().BeSameAs(setter, "both are views of the one ambient tenant");
    }

    [Fact]
    public void AddTenantry_RegistersWorkerScopeServicesAsSingletonsOnce()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();
        services.AddTenantry<string>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantScopeFactory<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantLookup<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddTenantry_WithAnotherKeyType_Throws()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();

        var act = () => services.AddTenantry<Guid>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'String'*'Guid'*one tenant key type*");
    }

    [Fact]
    public void UseStore_Factory_RegistersAScopedFactoryStore()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(builder => builder.UseStore(_ => new InMemoryTenantStore<string>([])));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantStore<string>>().Should().BeOfType<InMemoryTenantStore<string>>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantStore<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void UseStore_Generic_RegistersAScopedStoreType()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(builder => builder.UseStore<StubTenantStore>());
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantStore<string>>().Should().BeOfType<StubTenantStore>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantStore<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void ASecondStore_Throws_WhicheverWayEitherIsRegistered()
    {
        Action<ITenantBuilder<string>>[] stores =
        [
            tenant => tenant.UseStore<StubTenantStore>(),
            tenant => tenant.UseStore(_ => new StubTenantStore()),
            tenant => tenant.UseInMemoryStore([]),
        ];

        foreach (var first in stores)
        {
            foreach (var second in stores)
            {
                ServiceCollection services = new();
                services.AddTenantry(first);

                var act = () => services.AddTenantry(second);

                act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*one store*");
            }
        }
    }

    [Fact]
    public void AStoreRegisteredDirectly_CountsAsTheStore()
    {
        ServiceCollection services = new();
        services.AddScoped<ITenantStore<string>, StubTenantStore>();

        var act = () => services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([]));

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Chaining_EveryCoreBuilderMethodReturnsTheBuilder()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant
            .UseStore<StubTenantStore>()
            .UseConnectionStrings(options => options.GetConnectionString = t => t.TenantId));

        services.Should().Contain(d => d.ServiceType == typeof(ITenantConnectionStringProvider<string>));
    }

    [Fact]
    public void Add_AppliesTheRegistrationWithTheBuildersKeyType()
    {
        ServiceCollection services = new();
        RecordingRegistration registration = new();

        services.AddTenantry<Guid>(tenant => tenant.Add(registration));

        registration.KeyType.Should().Be<Guid>();
        registration.Services.Should().BeSameAs(services);
    }

    private sealed class RecordingRegistration : ITenantRegistration
    {
        public Type? KeyType { get; private set; }

        public IServiceCollection? Services { get; private set; }

        public void Apply<TKey>(ITenantBuilder<TKey> tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            KeyType = typeof(TKey);
            Services = tenant.Services;
        }
    }

    [Fact]
    public void TheKeyType_CanBeReadFromTheServicesAndTheProvider()
    {
        ServiceCollection services = new();
        services.FindTenantKeyType().Should().BeNull();

        services.AddTenantry<Guid>();

        services.FindTenantKeyType()!.Type.Should().Be<Guid>();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITenantKeyType>().Accept(new KeyTypeName()).Should().Be(nameof(Guid));
    }

    private sealed class KeyTypeName : ITenantKeyTypeVisitor<string>
    {
        public string Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey> => typeof(TKey).Name;
    }

    private sealed class StubTenantStore : ITenantStore<string>
    {
        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ITenantDescriptor<string>?>(null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([]);
    }
}
