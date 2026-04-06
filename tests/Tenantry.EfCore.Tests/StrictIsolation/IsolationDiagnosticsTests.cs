using AwesomeAssertions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.StrictIsolation;

public sealed class IsolationDiagnosticsTests
{
    [Fact]
    public void Properties_AreStoredCorrectly()
    {
        IsolationDiagnostics diag = new()
        {
            EntityTypeName = "Order",
            OffendingTenantId = "attacker",
            ExpectedTenantId = "acme",
        };

        diag.EntityTypeName.Should().Be("Order");
        diag.OffendingTenantId.Should().Be("attacker");
        diag.ExpectedTenantId.Should().Be("acme");
    }

    [Fact]
    public void DetectedAt_IsRecentUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        IsolationDiagnostics diag = new()
        {
            EntityTypeName = "Order",
            OffendingTenantId = "attacker",
            ExpectedTenantId = "acme",
        };

        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        diag.DetectedAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void DetectedAt_IsUtc()
    {
        IsolationDiagnostics diag = new()
        {
            EntityTypeName = "Order",
            OffendingTenantId = "attacker",
            ExpectedTenantId = "acme",
        };

        diag.DetectedAt.Offset.Should().Be(TimeSpan.Zero);
    }
}
