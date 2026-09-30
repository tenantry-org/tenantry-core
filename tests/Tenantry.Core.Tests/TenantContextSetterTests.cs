using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

public sealed class TenantContextSetterTests
{
    private static ITenantContextSetter<string> BuildSetter()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();
        return services.BuildServiceProvider().GetRequiredService<ITenantContextSetter<string>>();
    }

    [Fact]
    public void Use_SetsCurrentTenantAndHasTenant()
    {
        var tenantContext = BuildSetter();
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        using (tenantContext.Use(descriptor))
        {
            tenantContext.HasTenant.Should().BeTrue();
            tenantContext.CurrentTenant.Should().Be(descriptor);
        }

        tenantContext.HasTenant.Should().BeFalse();
        tenantContext.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void Use_WhenNested_ShadowsOuterThenRestoresOnDispose()
    {
        var tenantContext = BuildSetter();
        TenantDescriptor<string> outer = new() { TenantId = "acme", Name = "Acme Corp" };
        TenantDescriptor<string> inner = new() { TenantId = "globex", Name = "Globex" };

        using (tenantContext.Use(outer))
        {
            tenantContext.CurrentTenant.Should().Be(outer);

            using (tenantContext.Use(inner))
            {
                tenantContext.CurrentTenant.Should().Be(inner, "the inner tenantContext shadows the outer tenant");
            }

            tenantContext.CurrentTenant.Should().Be(outer, "disposing the inner tenantContext restores the outer tenant");
        }

        tenantContext.HasTenant.Should().BeFalse("disposing the outermost tenantContext restores 'no tenant'");
        tenantContext.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotDisturbALaterScope()
    {
        var tenantContext = BuildSetter();
        var first = tenantContext.Use(Tenant("acme"));
        first.Dispose();

        using (tenantContext.Use(Tenant("globex")))
        {
            first.Dispose();

            tenantContext.CurrentTenantId.Should().Be("globex", "a second dispose of a closed tenantContext does nothing");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void Dispose_OutOfOrder_KeepsTheInnerScopeAndThenRestoresNoTenant()
    {
        var tenantContext = BuildSetter();
        var outer = tenantContext.Use(Tenant("acme"));
        var inner = tenantContext.Use(Tenant("globex"));

        outer.Dispose();
        tenantContext.CurrentTenantId.Should().Be("globex", "closing the outer tenantContext first leaves the inner one active");

        inner.Dispose();
        tenantContext.HasTenant.Should().BeFalse("the outer tenantContext is already closed, so it is not restored");
    }

    [Fact]
    public void Dispose_MiddleScopeFirst_RestoresTheNearestOpenScope()
    {
        var tenantContext = BuildSetter();
        using var outer = tenantContext.Use(Tenant("acme"));
        var middle = tenantContext.Use(Tenant("globex"));
        var inner = tenantContext.Use(Tenant("initech"));

        middle.Dispose();
        inner.Dispose();

        tenantContext.CurrentTenantId.Should().Be("acme");
    }

    [Fact]
    public async Task Dispose_FromAnotherAsyncFlow_DoesNotChangeTheCallersTenant()
    {
        var tenantContext = BuildSetter();

        using (tenantContext.Use(Tenant("acme")))
        {
            // The tenantContext begins inside Task.Run, so it is never visible to this flow.
            var handle = await Task.Run(() => tenantContext.Use(Tenant("globex")));

            handle.Dispose();

            tenantContext.CurrentTenantId.Should().Be("acme");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_ByAChildFlowFirst_StillRestoresTheCallerWhenTheCallerDisposes()
    {
        var tenantContext = BuildSetter();

        using (tenantContext.Use(Tenant("old")))
        {
            var handle = tenantContext.Use(Tenant("acme"));

            // The child flow inherits the tenantContext and closes it. Its restore only affects the child's own flow.
            await Task.Run(handle.Dispose);
            tenantContext.CurrentTenantId.Should().Be("acme", "a child flow cannot change the caller's ambient tenant");

            handle.Dispose();
            tenantContext.CurrentTenantId.Should().Be("old", "the caller's own disposal still restores its previous tenant");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task UsingBlock_WhoseHandleAChildFlowDisposed_RestoresNoTenant()
    {
        var tenantContext = BuildSetter();

        using (var handle = tenantContext.Use(Tenant("acme")))
        {
            await Task.Run(handle.Dispose);
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_OfAnOuterScopeByAChildFlow_IsSkippedWhenTheCallersInnerScopeCloses()
    {
        var tenantContext = BuildSetter();
        var outer = tenantContext.Use(Tenant("acme"));
        var inner = tenantContext.Use(Tenant("globex"));

        await Task.Run(outer.Dispose);
        tenantContext.CurrentTenantId.Should().Be("globex");

        inner.Dispose();
        tenantContext.HasTenant.Should().BeFalse("the outer tenantContext was already closed, so it is not restored");

        outer.Dispose();
        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_OfTheInnerScopeByAChildFlow_ThenByTheCaller_RestoresTheOuterScope()
    {
        var tenantContext = BuildSetter();

        using (tenantContext.Use(Tenant("acme")))
        {
            var inner = tenantContext.Use(Tenant("globex"));
            await Task.Run(inner.Dispose);

            inner.Dispose();

            tenantContext.CurrentTenantId.Should().Be("acme");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void Use_TenantWithTheKeyTypesDefaultId_ThrowsAndLeavesNoTenant()
    {
        var strings = BuildSetter();
        var guids = Setter<Guid>();
        var ints = Setter<int>();

        FluentActions.Invoking(() => strings.Use(new TenantDescriptor<string> { TenantId = "", Name = "Empty" }))
            .Should().Throw<ArgumentException>().WithMessage("*reserves*\"no tenant\"*");
        FluentActions.Invoking(() => strings.Use(new TenantDescriptor<string> { TenantId = null!, Name = "Null" }))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => guids.Use(new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" }))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ints.Use(new TenantDescriptor<int> { TenantId = 0, Name = "Zero" }))
            .Should().Throw<ArgumentException>();

        strings.HasTenant.Should().BeFalse();
        guids.HasTenant.Should().BeFalse();
        ints.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void CurrentTenantId_WithoutATenant_IsTheKeyTypesDefault()
    {
        Setter<Guid>().CurrentTenantId.Should().Be(Guid.Empty);
        Setter<int>().CurrentTenantId.Should().Be(0);
        BuildSetter().CurrentTenantId.Should().BeNull();
    }

    private static ITenantContextSetter<TKey> Setter<TKey>()
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ServiceCollection services = new();
        services.AddTenantry<TKey>();
        return services.BuildServiceProvider().GetRequiredService<ITenantContextSetter<TKey>>();
    }

    private static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };
}
