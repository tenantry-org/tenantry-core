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
        provider.GetRequiredService<IOptions<TenantResolutionOptions<string>>>().Value.RequireTenantByDefault.Should().BeTrue();
        provider.GetServices<ITenantAccessValidator<string>>().Should().HaveCount(2);
    }

    [Fact]
    public void ConfigureResolution_SetsTheStatusCodesAndEvents()
    {
        Func<TenantResolvedContext<string>, Task> onResolved = _ => Task.CompletedTask;
        Func<TenantRejectedContext<string>, Task> onRejected = _ => Task.CompletedTask;
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.ConfigureResolution(options =>
        {
            options.MissingTenantStatusCode = 401;
            options.TenantNotFoundStatusCode = 410;
            options.AccessDeniedStatusCode = 404;
            options.OnResolved = onResolved;
            options.OnRejected = onRejected;
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TenantResolutionOptions<string>>>().Value;

        options.Should().BeEquivalentTo(new
        {
            RequireTenantByDefault = false,
            MissingTenantStatusCode = 401,
            TenantNotFoundStatusCode = 410,
            AccessDeniedStatusCode = 404,
        });
        options.OnResolved.Should().BeSameAs(onResolved);
        options.OnRejected.Should().BeSameAs(onRejected);
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ITenantResolutionMiddlewareConfigurator));
    }

    [Fact]
    public void TenantResolutionOptions_Defaults()
    {
        new TenantResolutionOptions<Guid>().Should().BeEquivalentTo(new
        {
            RequireTenantByDefault = false,
            MissingTenantStatusCode = 400,
            TenantNotFoundStatusCode = 404,
            AccessDeniedStatusCode = 403,
            OnResolved = (Func<TenantResolvedContext<Guid>, Task>?)null,
            OnRejected = (Func<TenantRejectedContext<Guid>, Task>?)null,
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
            .ResolveFromSubdomain(options => options.BaseDomains.Add("example.com"))
            .ResolveFromHost()
            .UseResolver(new TestTenantResolver())
            .UseResolver(_ => new TestTenantResolver())
            .RequireTenantByDefault()
            .ConfigureResolution(options => options.AccessDeniedStatusCode = 404)
            .ValidateTenantAccessByClaim("tenants")
            .ValidateTenantAccess((_, _) => true)
            .ValidateTenantAccess((_, _, _) => ValueTask.FromResult(true))
            .UseInMemoryStore([])
            .CacheTenants()
            .UseResolver<TestTenantResolver>()
            .ValidateTenantAccess<TestValidator>());

        services.Count(sd => sd.ServiceType == typeof(ITenantResolver)).Should().Be(9);
        services.Count(sd => sd.ServiceType == typeof(ITenantAccessValidator<Guid>)).Should().Be(4);
    }

    [Fact]
    public void ValidateTenantAccess_OfAType_IsCreatedInEachRequestsScope_Once()
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .ValidateTenantAccess<TestValidator>()
            .ValidateTenantAccess<TestValidator>());

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ITenantAccessValidator<Guid>))
            .Which.Should().BeEquivalentTo(new
            {
                Lifetime = ServiceLifetime.Scoped,
                ImplementationType = typeof(TestValidator),
            });
    }

    [Fact]
    public void ValidateTenantAccess_OfATypeForAnotherKeyType_Throws()
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.ValidateTenantAccess<TestValidator>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("TestValidator does not implement ITenantAccessValidator<String>*tenant key type 'String'*");
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
    public void ResolveFromHost_RegistersHostTenantResolver()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.ResolveFromHost());

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationInstance is HostTenantResolver);
    }

    [Fact]
    public void UseResolver_Generic_RegistersCustomResolverInEachRequestsScope()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseResolver<TestTenantResolver>();
            tenant.UseInMemoryStore([]);
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITenantResolver) &&
            sd.ImplementationType == typeof(TestTenantResolver) &&
            sd.Lifetime == ServiceLifetime.Scoped);
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
            sd.ImplementationFactory != null &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed class TestValidator : ITenantAccessValidator<Guid>
    {
        public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<Guid> tenant, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class TestTenantResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("test-tenant");
    }
}
