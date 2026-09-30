using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Verifies that the ASP.NET Core builder methods add the correct services to the service collection.
/// </summary>
public sealed class BuilderRegistrationTests
{
    [Fact]
    public void AddTenantry_CalledTwice_KeepsTheFirstCallsSettings()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore([]);
            tenant.RequireTenantByDefault();
            tenant.ValidateTenantAccess((_, _) => true);
        });
        services.AddTenantry<string>(tenant => tenant.ValidateTenantAccessByClaim("tenants"));

        services.Count(sd => sd.ServiceType == typeof(ITenantResolutionMiddlewareConfigurator)).Should().Be(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value.RequireTenantByDefault.Should().BeTrue();
        provider.GetRequiredService<IOptions<TenantAccessOptions<string>>>().Value.Validators.Should().HaveCount(2);
    }

    [Fact]
    public void ConfigureResolution_SetsTheStatusCodes()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.ConfigureResolution(options =>
        {
            options.MissingTenantStatusCode = 401;
            options.InvalidTenantStatusCode = 422;
            options.TenantNotFoundStatusCode = 410;
            options.AccessDeniedStatusCode = 404;
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TenantResolutionOptions>>().Value;

        options.Should().BeEquivalentTo(new
        {
            RequireTenantByDefault = false,
            MissingTenantStatusCode = 401,
            InvalidTenantStatusCode = 422,
            TenantNotFoundStatusCode = 410,
            AccessDeniedStatusCode = 404,
        });
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ITenantResolutionMiddlewareConfigurator));
    }

    [Fact]
    public void TenantResolutionOptions_Defaults()
    {
        new TenantResolutionOptions().Should().BeEquivalentTo(new
        {
            RequireTenantByDefault = false,
            MissingTenantStatusCode = 400,
            InvalidTenantStatusCode = 400,
            TenantNotFoundStatusCode = 404,
            AccessDeniedStatusCode = 403,
        });
    }

    [Fact]
    public void EveryBuilderMethod_Chains()
    {
        ServiceCollection services = new();

        services.AddTenantry<Guid>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .ResolveFromClaim()
            .ResolveFromRouteValue()
            .ResolveFromQueryString()
            .ResolveFromSubdomain(options => options.BaseDomain = "example.com")
            .UseResolver(new TestTenantResolver())
            .UseResolver(_ => new TestTenantResolver())
            .RequireTenantByDefault()
            .ConfigureResolution(options => options.AccessDeniedStatusCode = 404)
            .ValidateTenantAccessByClaim("tenants")
            .ValidateTenantAccess((_, _) => true)
            .ValidateTenantAccess((_, _, _) => ValueTask.FromResult(true))
            .UseInMemoryStore([])
            .UseResolver<TestTenantResolver>());

        services.Count(sd => sd.ServiceType == typeof(ITenantResolver)).Should().Be(8);
    }

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
            sd.ImplementationInstance is SubdomainTenantResolver);
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

    private sealed class TestTenantResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("test-tenant");
    }
}
