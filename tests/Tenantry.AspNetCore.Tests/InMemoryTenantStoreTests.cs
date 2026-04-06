using AwesomeAssertions;
using Tenantry.Core;
using Tenantry.Core.Stores;

namespace Tenantry.AspNetCore.Tests;

public sealed class InMemoryTenantStoreTests
{
    private static readonly ITenantDescriptor<string>[] Tenants =
    [
        new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
    ];

    private readonly InMemoryTenantStore<string> _store = new(Tenants);

    [Fact]
    public async Task GetTenantAsync_KnownId_ReturnsTenant()
    {
        var result = await _store.GetTenantAsync("acme");

        result.Should().NotBeNull();
        result.TenantId.Should().Be("acme");
        result.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task GetTenantAsync_UnknownId_ReturnsNull()
    {
        var result = await _store.GetTenantAsync("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllTenantsAsync_ReturnsAllTenants()
    {
        var result = await _store.GetAllTenantsAsync();

        result.Should().HaveCount(2);
        result.Select(t => t.TenantId).Should().Contain(["acme", "globex"]);
    }

    [Fact]
    public async Task GetTenantAsync_WithGuidKey_Works()
    {
        var id = Guid.NewGuid();
        InMemoryTenantStore<Guid> store = new([new TenantDescriptor<Guid> { TenantId = id, Name = "Test" }]);

        var result = await store.GetTenantAsync(id);

        result.Should().NotBeNull();
        result.TenantId.Should().Be(id);
    }
}
