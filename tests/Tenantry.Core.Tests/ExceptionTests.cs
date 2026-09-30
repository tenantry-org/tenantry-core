using AwesomeAssertions;

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
    public void TenantNotResolvedException_DerivesFromInvalidOperationException()
    {
        TenantNotResolvedException ex = new();

        ex.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void TenantNotResolvedException_DefaultMessage_NamesEveryHostsWayToMakeATenantCurrent()
    {
        TenantNotResolvedException ex = new();

        ex.Message.Should().Contain("app.UseTenantry()").And.Contain("ITenantScopeFactory");
    }

    [Fact]
    public void TenantNotFoundException_CarriesTheIdAndIsATenantNotResolvedException()
    {
        var id = Guid.NewGuid();

        TenantNotFoundException ex = new(id);

        ex.TenantId.Should().Be(id);
        ex.Message.Should().Contain(id.ToString());
        ex.Should().BeAssignableTo<TenantNotResolvedException>();
    }

    [Fact]
    public void TenantNotFoundException_WithAMessage_KeepsBoth()
    {
        TenantNotFoundException ex = new("acme", "Custom message");

        ex.TenantId.Should().Be("acme");
        ex.Message.Should().Be("Custom message");
    }
}
