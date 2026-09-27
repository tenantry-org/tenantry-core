using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Extensions;

namespace Tenantry.Core.Tests;

public sealed class TenantStoreAccessorTests
{
    [Fact]
    public async Task ScopedStore_IsResolvedFreshForEachCallAndDisposed()
    {
        ScopedStore.Created.Clear();
        ServiceCollection services = new();
        services.AddTenantryCore<string>(tenant => tenant.UseStore<ScopedStore>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Resolved from the root provider, as a hosted service would. A captive scoped store would fail scope validation.
        var accessor = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        (await accessor.GetTenantAsync("acme"))!.TenantId.Should().Be("acme");
        (await accessor.GetAllTenantsAsync()).Select(t => t.TenantId).Should().Equal("acme", "globex");

        ScopedStore.Created.Should().HaveCount(2);
        ScopedStore.Created.Should().OnlyContain(store => store.Disposed);
    }

    [Fact]
    public async Task UnknownTenant_ReturnsNull()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>(tenant => tenant.UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }]));
        await using var provider = services.BuildServiceProvider();

        var tenant = await provider.GetRequiredService<ITenantStoreAccessor<string>>().GetTenantAsync("missing");

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task NoStoreRegistered_ThrowsAnErrorThatSaysHowToRegisterOne()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>();
        await using var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ITenantStoreAccessor<string>>().GetAllTenantsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseInMemoryStore*UseStore*");
    }

    private sealed class ScopedStore : ITenantStore<string>, IDisposable
    {
        public static readonly List<ScopedStore> Created = [];

        public ScopedStore() => Created.Add(this);

        public bool Disposed { get; private set; }

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<string>?>(new TenantDescriptor<string> { TenantId = tenantId, Name = tenantId });

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" },
            ]);

        public void Dispose() => Disposed = true;
    }
}
