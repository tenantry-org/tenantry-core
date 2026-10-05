using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>IgnoreWarnings</c> turns off the warnings about configuration that may be deliberate, and refuses every other
/// Tenantry event. Ids outside Tenantry's are left to the package that logs them.
/// </summary>
public sealed class IgnoreWarningsTests
{
    [Fact]
    public void TheNamedWarnings_AreIgnored_AndNoOthers()
    {
        using var sp = Build(tenant => tenant.IgnoreWarnings(
            TenantryWarnings.StringTenantIdCollation, TenantryWarnings.OrdinaryOptionsReadAsTenant));

        TenantryWarnings.IsIgnored(sp, 2007).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 3001).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 4402).Should().BeFalse();
    }

    [Fact]
    public void EveryWarningTenantryWarningsNames_IsAccepted()
    {
        var eventIds = typeof(TenantryWarnings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();

        using var sp = Build(tenant => tenant.IgnoreWarnings(eventIds));

        eventIds.Should().HaveCountGreaterThanOrEqualTo(2).And.OnlyContain(eventId => TenantryWarnings.IsIgnored(sp, eventId));
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
        services.AddTenantry<string>(tenant => tenant
            .IgnoreWarnings(TenantryWarnings.StringTenantIdCollation)
            .IgnoreWarnings(4403));
        services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.OrdinaryOptionsReadAsTenant));
        using var sp = services.BuildServiceProvider();

        TenantryWarnings.IsIgnored(sp, 2007).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 3001).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 4403).Should().BeTrue();
    }

    [Theory]
    [InlineData(4402)]
    [InlineData(4999)]
    [InlineData(999)]
    [InlineData(0)]
    public void AnIdOutsideTenantrysEvents_IsAcceptedForThePackageThatLogsIt(int eventId)
    {
        using var sp = Build(tenant => tenant.IgnoreWarnings(eventId));

        TenantryWarnings.IsIgnored(sp, eventId).Should().BeTrue();
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
    [InlineData(1000)]
    [InlineData(3999)]
    public void AnyOtherTenantryEvent_IsRefused_NamingIt(int eventId)
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation, eventId));

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Tenantry cannot ignore event {eventId}:*2007 and 3001 (TenantryWarnings)*OnMissingTenant*OnUnmarkedEntityType*")
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
