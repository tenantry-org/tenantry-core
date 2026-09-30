using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests;

public sealed class EfCoreIsolationRegistrationTests
{
    [Fact]
    public void AddEfCoreIsolation_Default_RegistersTheInterceptorAndOptions()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant.AddEfCoreIsolation());

        services.Should().Contain(sd => sd.ServiceType == typeof(TenantSaveChangesInterceptor<string>));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantInterceptorConfigurator));
        services.Should().Contain(sd => sd.ServiceType == typeof(EfCoreIsolationOptions));
    }

    [Fact]
    public void AddEfCoreIsolation_Default_UsesTheRejectPolicy()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant.AddEfCoreIsolation());

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<EfCoreIsolationOptions>().OnMissingTenant.Should().Be(MissingTenantBehavior.Reject);
    }

    [Fact]
    public void AddEfCoreIsolation_CapturesConfiguredPolicy()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant.AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<EfCoreIsolationOptions>().OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
    }

    [Fact]
    public void AddEfCoreIsolation_CalledTwice_ConfiguresTheSameOptions()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant
            .AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Allow)
            .AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));

        services.Count(sd => sd.ServiceType == typeof(EfCoreIsolationOptions)).Should().Be(1);
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<EfCoreIsolationOptions>().OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
    }

    [Fact]
    public void AddTenantInterceptors_WithoutEfCoreIsolation_SaysWhatToCall()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();
        using var sp = services.BuildServiceProvider();

        var act = () => new DbContextOptionsBuilder().AddTenantInterceptors(sp);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddEfCoreIsolation*");
    }

    [Fact]
    public void AddTenantInterceptors_WithEfCoreIsolationRegistered_AttachesInterceptorToOptions()
    {
        // Exercises TenantInterceptorConfigurator.AddInterceptors (resolves TenantSaveChangesInterceptor
        // from DI) and DbContextOptionsBuilderExtensions.AddTenantInterceptors (resolves the configurator).
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(builder => builder.AddEfCoreIsolation());

        using var sp = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder();
        var result = optionsBuilder.AddTenantInterceptors(sp);

        result.Should().BeSameAs(optionsBuilder);
    }

    [Fact]
    public void AddTenantInterceptors_CalledTwice_IsIdempotent()
    {
        // Exercises the early-return branch in TenantInterceptorConfigurator.AddInterceptors
        // that skips re-adding the interceptor when it is already present.
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(builder => builder.AddEfCoreIsolation());

        using var sp = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder();
        optionsBuilder.AddTenantInterceptors(sp);
        var result = optionsBuilder.AddTenantInterceptors(sp);

        result.Should().BeSameAs(optionsBuilder);
        var interceptors = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()!.Interceptors!.ToList();
        interceptors.OfType<TenantSaveChangesInterceptor<string>>().Should().ContainSingle();
        interceptors.OfType<TenantBulkUpdateGuard<string>>().Should().ContainSingle();
    }

}
