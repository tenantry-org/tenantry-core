using AwesomeAssertions;
using Tenantry.AspNetCore.Extensions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Verifies that AspNetCoreTenantBuilder registration methods add the correct
/// ITenantResolver services to the service collection.
/// </summary>
public sealed class BuilderRegistrationTests
{
    [Fact]
    public void ResolveFromClaim_RegistersClaimTenantResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromClaim();
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationInstance is ClaimTenantResolver);
    }

    [Fact]
    public void ResolveFromRouteValue_RegistersRouteValueTenantResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromRouteValue();
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationInstance is RouteValueTenantResolver);
    }

    [Fact]
    public void ResolveFromSubdomain_RegistersSubdomainTenantResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromSubdomain();
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationType == typeof(SubdomainTenantResolver));
    }

    [Fact]
    public void UseResolver_Generic_RegistersCustomResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseResolver<TestTenantResolver>();
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationType == typeof(TestTenantResolver));
    }

    [Fact]
    public void UseResolver_Instance_RegistersCustomResolver()
    {
        var resolver = new TestTenantResolver();

        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseResolver(resolver);
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationInstance == resolver);
    }

    [Fact]
    public void UseResolver_Factory_RegistersCustomResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseResolver(_ => new TestTenantResolver());
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationFactory != null);
    }

    [Fact]
    public void ValidateTenantAccessAny_WithEmptyGroups_ThrowsArgumentException()
    {
        var act = () => new ServiceCollection().AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore([]);
            tenant.ValidateTenantAccessAny(); // empty params array
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one validation group must be provided*");
    }

    private sealed class TestTenantResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("test-tenant");
    }
}
