using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/>: by default an identifier is a tenant id, parsed with the
/// invariant culture; a store can map other names to its tenants.
/// </summary>
public sealed class TenantIdentifierTests
{
    [Fact]
    public async Task ByDefault_TheIdentifierIsTheTenantId_ParsedWithTheInvariantCulture()
    {
        ITenantStore<int> store = new InMemoryTenantStore<int>([new TenantDescriptor<int> { TenantId = -5, Name = "Negative" }]);

        // A culture whose negative sign is not '-' cannot parse "-5"; the identifier is parsed regardless of it.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;

        try
        {
            int.TryParse("-5", null, out _).Should().BeFalse("the test's culture must not parse the identifier");
            (await store.FindByIdentifierAsync("-5"))!.Name.Should().Be("Negative");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task ByDefault_AnIdentifierThatDoesNotParse_OrIsTheReservedId_NamesNoTenant_WithoutAskingTheStore(string identifier)
    {
        CountingStore store = new([new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" }]);

        (await ((ITenantStore<Guid>)store).FindByIdentifierAsync(identifier)).Should().BeNull();
        store.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task ByDefault_AnEmptyStringId_NamesNoTenant()
    {
        ITenantStore<string> store = new InMemoryTenantStore<string>([new TenantDescriptor<string> { TenantId = "", Name = "Empty" }]);

        (await store.FindByIdentifierAsync("")).Should().BeNull();
    }

    [Fact]
    public async Task TheAccessor_FindsTenantsWithTheStoresOwnMapping()
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant.UseStore<SlugStore>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<Guid>>();

        (await tenants.FindByIdentifierAsync("acme"))!.TenantId.Should().Be(SlugStore.Acme);
        (await tenants.FindByIdentifierAsync(SlugStore.Acme.ToString())).Should().BeNull("this store maps slugs only");
        (await tenants.FindByIdentifierAsync("globex")).Should().BeNull();
    }

    [Fact]
    public async Task TheAccessor_RequiresAnIdentifier()
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant.UseStore<SlugStore>());
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITenantStoreAccessor<Guid>>()
            .Invoking(t => t.FindByIdentifierAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class SlugStore : ITenantStore<Guid>
    {
        public static readonly Guid Acme = Guid.NewGuid();

        private static readonly TenantDescriptor<Guid> AcmeTenant = new() { TenantId = Acme, Name = "Acme" };

        public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<Guid>?>(tenantId == Acme ? AcmeTenant : null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<Guid>>>([AcmeTenant]);

        public ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<Guid>?>(identifier == "acme" ? AcmeTenant : null);
    }

    private sealed class CountingStore(IEnumerable<ITenantDescriptor<Guid>> tenants) : ITenantStore<Guid>
    {
        private readonly InMemoryTenantStore<Guid> _inner = new(tenants);

        public int Lookups { get; private set; }

        public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return _inner.GetTenantAsync(tenantId, cancellationToken);
        }

        public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            _inner.GetAllTenantsAsync(cancellationToken);
    }
}
