using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>IgnoreWarnings</c> turns off the warnings about configuration that may be deliberate, and refuses every other
/// id.
/// </summary>
public sealed class IgnoreWarningsTests
{
    [Fact]
    public void TheNamedWarnings_AreIgnored_AndNoOthers()
    {
        using var sp = Build(tenant => tenant.IgnoreWarnings(
            TenantryWarnings.StringTenantIdCollation, TenantryWarnings.OrdinaryOptionsReadAsTenant));

        TenantryWarnings.IsIgnored(sp, 2007).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 2008).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 2006).Should().BeFalse();
    }

    [Fact]
    public void EveryWarningTenantryWarningsNames_IsAccepted()
    {
        var eventIds = typeof(TenantryWarnings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();

        using var sp = Build(tenant => tenant.IgnoreWarnings(eventIds));

        eventIds.Should().HaveCountGreaterThanOrEqualTo(2);
        foreach (var eventId in eventIds)
        {
            TenantryWarnings.IsIgnored(sp, eventId).Should().BeTrue($"TenantryWarnings names {eventId}");
        }
    }

    [Fact]
    public void ACallAfterTheProviderIsBuilt_DoesNotChangeIt()
    {
        ITenantBuilder<string>? captured = null;
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => captured = tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation));
        using var sp = services.BuildServiceProvider();

        captured!.IgnoreWarnings(TenantryWarnings.OrdinaryOptionsReadAsTenant);

        TenantryWarnings.IsIgnored(sp, TenantryWarnings.StringTenantIdCollation).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, TenantryWarnings.OrdinaryOptionsReadAsTenant).Should().BeFalse();
    }

    [Fact]
    public void WithoutIgnoreWarnings_NothingIsIgnored()
    {
        using var sp = Build(_ => { });

        TenantryWarnings.IsIgnored(sp, TenantryWarnings.StringTenantIdCollation).Should().BeFalse();
    }

    [Fact]
    public void EveryCall_AddsToTheIgnoredWarnings_AlsoInAnotherAddTenantry()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation));
        services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.OrdinaryOptionsReadAsTenant));
        using var sp = services.BuildServiceProvider();

        TenantryWarnings.IsIgnored(sp, 2007).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 2008).Should().BeTrue();
    }

    [Theory]
    [InlineData(1003)] // a request refused
    [InlineData(1007)] // middleware in the wrong order
    [InlineData(1014)] // logged once at startup, but an ordering check lost
    [InlineData(1016)]
    [InlineData(2001)] // an isolation violation
    [InlineData(2002)] // a write without a tenant
    [InlineData(2003)] // a write that matched no row
    [InlineData(2006)] // turned off with OnUnmarkedEntityType
    [InlineData(3001)] // Tenantry.Pro's events, 3000 and above
    [InlineData(3002)]
    [InlineData(4402)]
    [InlineData(999)]
    [InlineData(0)]
    public void AnyOtherId_IsRefused_NamingIt(int eventId)
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation, eventId));

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Tenantry cannot ignore event {eventId}. IgnoreWarnings accepts only 2007 and 2008 " +
                "(TenantryWarnings). Event 2002 *OnMissingTenant*OnUnmarkedEntityType*")
            .Which.ParamName.Should().Be("eventIds");
        services.AddTenantry<string>();
        using var sp = services.BuildServiceProvider();
        TenantryWarnings.IsIgnored(sp, TenantryWarnings.StringTenantIdCollation).Should().BeFalse("nothing of a refused call is kept");
    }

    [Fact]
    public void NullArguments_Throw()
    {
        ServiceCollection services = new();

        FluentActions.Invoking(() => services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(null!)))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => TenantryWarnings.IsIgnored(null!, 2007)).Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Build(Action<ITenantBuilder<string>> configure)
    {
        ServiceCollection services = new();
        services.AddTenantry(configure);
        return services.BuildServiceProvider();
    }
}
