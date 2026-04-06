using AwesomeAssertions;
using Tenantry.Core.Exceptions;

namespace Tenantry.Core.Tests;

public sealed class ExceptionTests
{
    [Fact]
    public void TenantNotResolvedException_DefaultCtor_HasExpectedMessage()
    {
        TenantNotResolvedException ex = new();

        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void TenantNotResolvedException_IsException()
    {
        TenantNotResolvedException ex = new();

        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void TenantIsolationViolationException_StoresEntityAndTenantIds()
    {
        TenantIsolationViolationException ex = new("Order", "attacker", "acme");

        ex.EntityTypeName.Should().Be("Order");
        ex.OffendingTenantId.Should().Be("attacker");
        ex.ExpectedTenantId.Should().Be("acme");
    }

    [Fact]
    public void TenantIsolationViolationException_MessageContainsEntityAndTenantIds()
    {
        TenantIsolationViolationException ex = new("Order", "attacker", "acme");

        ex.Message.Should().Contain("Order")
            .And.Contain("attacker")
            .And.Contain("acme");
    }

    [Fact]
    public void TenantIsolationViolationException_IsException()
    {
        TenantIsolationViolationException ex = new("Order", "attacker", "acme");

        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void TenantNotResolvedException_MessageOverload_StoresMessage()
    {
        TenantNotResolvedException ex = new("Custom message");

        ex.Message.Should().Be("Custom message");
    }

    [Fact]
    public void TenantNotResolvedException_MessageAndInnerException_StoresBoth()
    {
        InvalidOperationException inner = new("inner");

        TenantNotResolvedException ex = new("Custom message", inner);

        ex.Message.Should().Be("Custom message");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void TenantIsolationViolationException_DerivesFromInvalidOperationException()
    {
        TenantIsolationViolationException ex = new("Order", "attacker", "acme");

        ex.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void TenantNotResolvedException_DerivesFromInvalidOperationException()
    {
        TenantNotResolvedException ex = new();

        ex.Should().BeAssignableTo<InvalidOperationException>();
    }
}
