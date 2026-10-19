using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>IgnoreWarnings</c> turns off the warnings about configuration that may be deliberate, those <c>TenantryWarnings</c>
/// names and those an integration registered, and refuses every other id.
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
    [InlineData(3001)] // Tenantry.Pro's events, 3000 to 9999
    [InlineData(3002)]
    [InlineData(4402)]
    [InlineData(10001)] // another integration's event, not registered
    [InlineData(999)]
    [InlineData(0)]
    public void AnyOtherId_IsRefused_NamingIt(int eventId)
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation, eventId));

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Tenantry cannot ignore event {eventId}. IgnoreWarnings accepts 2007 and 2008 " +
                "(TenantryWarnings) and the ids an integration registered before it was called (none). The " +
                "documentation's diagnostics page says how each other warning is turned off.*")
            .Which.ParamName.Should().Be("eventIds");
        services.AddTenantry<string>();
        using var sp = services.BuildServiceProvider();
        TenantryWarnings.IsIgnored(sp, TenantryWarnings.StringTenantIdCollation).Should().BeFalse("nothing of a refused call is kept");
    }

    [Fact]
    public void AnIdAnIntegrationRegistered_IsAccepted_AndAddsToTheNamedOnes()
    {
        using var sp = Build(tenant =>
        {
            TenantryWarnings.Register(tenant.Services, 10001, 10002);
            TenantryWarnings.Register(tenant.Services, 10003);
            tenant.IgnoreWarnings(10001, 10003, TenantryWarnings.StringTenantIdCollation);
        });

        TenantryWarnings.IsIgnored(sp, 10001).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 10003).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, TenantryWarnings.StringTenantIdCollation).Should().BeTrue();
        TenantryWarnings.IsIgnored(sp, 10002).Should().BeFalse("registered, but not ignored");
    }

    [Fact]
    public void AnIdRegisteredAfterIgnoreWarnings_IsRefused_NamingTheRegisteredOnes()
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant =>
        {
            TenantryWarnings.Register(tenant.Services, 10001, 10002);
            tenant.IgnoreWarnings(10003);
            TenantryWarnings.Register(tenant.Services, 10003);
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("Tenantry cannot ignore event 10003. IgnoreWarnings accepts 2007 and 2008 (TenantryWarnings) " +
                "and the ids an integration registered before it was called (10001, 10002).*");
    }

    [Theory]
    [InlineData(2002)] // turned off with OnMissingTenant; nothing reads IsIgnored for it
    [InlineData(2999)]
    [InlineData(0)]
    public void RegisteringOneOfTenantrysOwnIds_IsRefused(int eventId)
    {
        ServiceCollection services = new();

        FluentActions.Invoking(() => TenantryWarnings.Register(services, 3000, eventId))
            .Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("eventIds");
    }

    [Fact]
    public void RegisteringAnId_DoesNotIgnoreIt()
    {
        using var sp = Build(tenant => TenantryWarnings.Register(tenant.Services, 10001));

        TenantryWarnings.IsIgnored(sp, 10001).Should().BeFalse();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        ServiceCollection services = new();

        FluentActions.Invoking(() => services.AddTenantry<string>(tenant => tenant.IgnoreWarnings(null!)))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => TenantryWarnings.IsIgnored(null!, 2007)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => TenantryWarnings.Register(null!, 10001)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => TenantryWarnings.Register(services, null!)).Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Build(Action<ITenantBuilder<string>> configure)
    {
        ServiceCollection services = new();
        services.AddTenantry(configure);
        return services.BuildServiceProvider();
    }
}
