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

        // The interface-typed local is the assertion: this only compiles if
        // TenantDescriptor<T> implements ITenantDescriptor<T>. CA1859 (prefer the
        // concrete type for perf) is intentionally suppressed — using the concrete
        // type here would defeat the purpose of the test.
#pragma warning disable CA1859
        ITenantDescriptor<string> iface = descriptor;
#pragma warning restore CA1859
        iface.TenantId.Should().Be("acme");
        iface.Name.Should().Be("Acme Corp");
    }
}
