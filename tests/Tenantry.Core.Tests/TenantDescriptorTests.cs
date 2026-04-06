using AwesomeAssertions;

namespace Tenantry.Core.Tests;

public sealed class TenantDescriptorTests
{
    [Fact]
    public void TenantInfo_Properties_RoundTrip()
    {
        TenantDescriptor<string> descriptor = new()
        {
            TenantId = "acme",
            Name = "Acme Corp",
        };

        descriptor.TenantId.Should().Be("acme");
        descriptor.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public void TenantInfo_WithGuidKey_Works()
    {
        var id = Guid.NewGuid();
        TenantDescriptor<Guid> descriptor = new() { TenantId = id, Name = "Acme Corp" };

        descriptor.TenantId.Should().Be(id);
    }

    [Fact]
    public void TenantInfo_ImplementsInterface()
    {
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        ITenantDescriptor<string> iface = descriptor;
        iface.TenantId.Should().Be("acme");
        iface.Name.Should().Be("Acme Corp");
    }
}
