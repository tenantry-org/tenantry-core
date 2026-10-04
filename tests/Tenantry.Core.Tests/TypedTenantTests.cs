using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// The application's own tenant type: <see cref="TenantDescriptorExtensions.As{TTenant}"/> and
/// <see cref="ITenantContext{TKey}.GetCurrentTenant{TTenant}"/>.
/// </summary>
public sealed class TypedTenantTests
{
    private static readonly AppTenant Acme = new() { TenantId = Guid.NewGuid(), Name = "Acme", Plan = "enterprise" };

    [Fact]
    public void As_ReturnsTheSameTenant_AsTheApplicationsType()
    {
        ITenantDescriptor<Guid> tenant = Acme;

        tenant.As<AppTenant>().Should().BeSameAs(Acme);
        tenant.As<AppTenant>().Plan.Should().Be("enterprise");
    }

    [Fact]
    public void As_AnotherType_ThrowsAnErrorNamingBothTypes()
    {
        ITenantDescriptor<Guid> tenant = new TenantDescriptor<Guid> { TenantId = Guid.NewGuid(), Name = "Globex" };

        tenant.Invoking(t => t.As<AppTenant>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Tenant 'Globex' is of type TenantDescriptor<Guid>, not AppTenant. Tenants are what the tenant store returns: make it return AppTenant*");
    }

    [Fact]
    public void As_ATypeNestedInAGenericType_ThrowsTheSameError()
    {
        ITenantDescriptor<Guid> tenant = new Outer<int>.NestedTenant { TenantId = Guid.NewGuid(), Name = "Nested" };

        tenant.Invoking(t => t.As<AppTenant>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Tenant 'Nested' is of type NestedTenant<Int32>, not AppTenant*");
    }

    [Fact]
    public void As_RequiresATenant() =>
        FluentActions.Invoking(() => ((ITenantDescriptor)null!).As<AppTenant>()).Should().Throw<ArgumentNullException>();

    [Fact]
    public void GetCurrentTenant_IsTheCurrentTenantAsTheApplicationsType_OrNullWithoutOne()
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>();
        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<ITenantContextSetter<Guid>>();

        context.GetCurrentTenant<AppTenant>().Should().BeNull();

        using (context.MakeCurrent(Acme))
        {
            context.GetCurrentTenant<AppTenant>().Should().BeSameAs(Acme);
        }

        using (context.MakeCurrent(new TenantDescriptor<Guid> { TenantId = Guid.NewGuid(), Name = "Globex" }))
        {
            context.Invoking(c => c.GetCurrentTenant<AppTenant>()).Should().Throw<InvalidOperationException>()
                .WithMessage("*of type TenantDescriptor<Guid>, not AppTenant*");
        }
    }

    // Generic so that the tenant type is nested in a generic type; T itself is not used.
    // ReSharper disable once UnusedTypeParameter
    private static class Outer<T>
    {
        public sealed class NestedTenant : TenantDescriptor<Guid>;
    }

    private sealed class AppTenant : ITenantDescriptor<Guid>
    {
        public required Guid TenantId { get; init; }

        public required string Name { get; init; }

        public required string Plan { get; init; }
    }
}
