using AwesomeAssertions;

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
        var result = await _store.GetTenantAsync("acme", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.TenantId.Should().Be("acme");
        result.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task GetTenantAsync_UnknownId_ReturnsNull()
    {
        var result = await _store.GetTenantAsync("unknown", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllTenantsAsync_ReturnsAllTenants()
    {
        var result = await _store.GetAllTenantsAsync(TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Select(t => t.TenantId).Should().Contain(["acme", "globex"]);
    }

    [Fact]
    public async Task GetTenantAsync_WithGuidKey_Works()
    {
        var id = Guid.NewGuid();
        InMemoryTenantStore<Guid> store = new([new TenantDescriptor<Guid> { TenantId = id, Name = "Test" }]);

        var result = await store.GetTenantAsync(id, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.TenantId.Should().Be(id);
    }

    [Fact]
    public void ATenantWithAReservedId_IsRefused()
    {
        var create = () => new InMemoryTenantStore<string>([new TenantDescriptor<string> { TenantId = "", Name = "Empty" }]);

        create.Should().Throw<ArgumentException>().WithMessage("*'Empty'*reserves*");
    }

    [Fact]
    public void TwoTenantsWithTheSameId_AreRefused()
    {
        var create = () => new InMemoryTenantStore<string>(
        [
            new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Again" },
        ]);

        create.Should().Throw<ArgumentException>().WithMessage("*'Acme Corp'*'Acme Again'*'acme'*");
    }

    [Fact]
    public void UseInMemoryStore_ChecksItsTenantsWhenRegistered()
    {
        ServiceCollection services = new();

        var register = () => services.AddTenantry<Guid>(tenant =>
            tenant.UseInMemoryStore([new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" }]));

        register.Should().Throw<ArgumentException>();
    }
}
