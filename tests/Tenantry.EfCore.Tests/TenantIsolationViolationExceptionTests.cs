using AwesomeAssertions;

namespace Tenantry.EfCore.Tests;

public sealed class TenantIsolationViolationExceptionTests
{
    [Fact]
    public void Constructor_StoresEveryProperty()
    {
        TenantIsolationViolationException ex = new(
            TenantIsolationViolationKind.TenantDatabaseMismatch, "NotesDbContext", "message", "acme", "globex");

        ex.Kind.Should().Be(TenantIsolationViolationKind.TenantDatabaseMismatch);
        ex.TypeName.Should().Be("NotesDbContext");
        ex.Message.Should().Be("message");
        ex.OffendingTenantId.Should().Be("acme");
        ex.ExpectedTenantId.Should().Be("globex");
        ex.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void TenantIds_AreNullWhenNotGiven()
    {
        TenantIsolationViolationException ex = new(TenantIsolationViolationKind.BulkUpdate, "Order", "message");

        ex.OffendingTenantId.Should().BeNull();
        ex.ExpectedTenantId.Should().BeNull();
    }
}
