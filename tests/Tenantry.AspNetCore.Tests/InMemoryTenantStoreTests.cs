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
    public void TwoStringIdsThatDifferOnlyInCase_AreRefused()
    {
        // A database whose collation ignores case would take them for one id, and match each tenant's rows to the other.
        var create = () => new InMemoryTenantStore<string>(
        [
            new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            new TenantDescriptor<string> { TenantId = "ACME", Name = "Acme Shouting" },
        ]);

        create.Should().Throw<ArgumentException>().WithMessage("*'Acme Corp'*'Acme Shouting'*'acme'*'ACME'*differ only in case*")
            .WithParameterName("tenants");
    }

    [Fact]
    public async Task AStringId_IsStillFoundOnlyAsWritten()
    {
        (await _store.GetTenantAsync("ACME", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public void UseInMemoryStore_RefusesStringIdsThatDifferOnlyInCase_WhenRegistered()
    {
        ServiceCollection services = new();

        var register = () => services.AddTenantry<string>(tenant => tenant.UseInMemoryStore(
        [
            new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            new TenantDescriptor<string> { TenantId = "Globex", Name = "Globex Again" },
        ]));

        register.Should().Throw<ArgumentException>().WithMessage("*differ only in case*");
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
