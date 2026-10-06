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
    public void MakeCurrent_SetsCurrentTenantAndHasTenant()
    {
        var tenantContext = BuildSetter();
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        using (tenantContext.MakeCurrent(descriptor))
        {
            tenantContext.HasTenant.Should().BeTrue();
            tenantContext.CurrentTenant.Should().Be(descriptor);
        }

        tenantContext.HasTenant.Should().BeFalse();
        tenantContext.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void RequiredTenant_ReturnsTheCurrentTenant_AndThrowsWithoutOne()
    {
        var tenantContext = BuildSetter();
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        using (tenantContext.MakeCurrent(descriptor))
        {
            tenantContext.RequiredTenant.Should().BeSameAs(descriptor);
        }

        FluentActions.Invoking(() => tenantContext.RequiredTenant).Should().Throw<TenantNotResolvedException>()
            .WithMessage("No tenant is current.*ITenantScopeFactory.");
    }

    [Fact]
    public void MakeCurrent_WhenNested_ShadowsOuterThenRestoresOnDispose()
    {
        var tenantContext = BuildSetter();
        TenantDescriptor<string> outer = new() { TenantId = "acme", Name = "Acme Corp" };
        TenantDescriptor<string> inner = new() { TenantId = "globex", Name = "Globex" };

        using (tenantContext.MakeCurrent(outer))
        {
            tenantContext.CurrentTenant.Should().Be(outer);

            using (tenantContext.MakeCurrent(inner))
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
        var first = tenantContext.MakeCurrent(Tenant("acme"));
        first.Dispose();

        using (tenantContext.MakeCurrent(Tenant("globex")))
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
        var outer = tenantContext.MakeCurrent(Tenant("acme"));
        var inner = tenantContext.MakeCurrent(Tenant("globex"));

        outer.Dispose();
        tenantContext.CurrentTenantId.Should().Be("globex", "closing the outer tenantContext first leaves the inner one active");

        inner.Dispose();
        tenantContext.HasTenant.Should().BeFalse("the outer tenantContext is already closed, so it is not restored");
    }

    [Fact]
    public void Dispose_MiddleScopeFirst_RestoresTheNearestOpenScope()
    {
        var tenantContext = BuildSetter();
        using var outer = tenantContext.MakeCurrent(Tenant("acme"));
        var middle = tenantContext.MakeCurrent(Tenant("globex"));
        var inner = tenantContext.MakeCurrent(Tenant("initech"));

        middle.Dispose();
        inner.Dispose();

        tenantContext.CurrentTenantId.Should().Be("acme");
    }

    [Fact]
    public async Task Dispose_FromAnotherAsyncFlow_DoesNotChangeTheCallersTenant()
    {
        var tenantContext = BuildSetter();

        using (tenantContext.MakeCurrent(Tenant("acme")))
        {
            // The tenantContext begins inside Task.Run, so it is never visible to this flow.
            var handle = await Task.Run(() => tenantContext.MakeCurrent(Tenant("globex")));

            handle.Dispose();

            tenantContext.CurrentTenantId.Should().Be("acme");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_ByAChildFlowFirst_StillRestoresTheCallerWhenTheCallerDisposes()
    {
        var tenantContext = BuildSetter();

        using (tenantContext.MakeCurrent(Tenant("old")))
        {
            var handle = tenantContext.MakeCurrent(Tenant("acme"));

            // The child flow inherits the tenantContext and closes it. Its restore only affects the child's own flow.
            await Task.Run(handle.Dispose, TestContext.Current.CancellationToken);
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

        using (var handle = tenantContext.MakeCurrent(Tenant("acme")))
        {
            await Task.Run(handle.Dispose, TestContext.Current.CancellationToken);
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_OfAnOuterScopeByAChildFlow_IsSkippedWhenTheCallersInnerScopeCloses()
    {
        var tenantContext = BuildSetter();
        var outer = tenantContext.MakeCurrent(Tenant("acme"));
        var inner = tenantContext.MakeCurrent(Tenant("globex"));

        await Task.Run(outer.Dispose, TestContext.Current.CancellationToken);
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

        using (tenantContext.MakeCurrent(Tenant("acme")))
        {
            var inner = tenantContext.MakeCurrent(Tenant("globex"));
            await Task.Run(inner.Dispose, TestContext.Current.CancellationToken);

            inner.Dispose();

            tenantContext.CurrentTenantId.Should().Be("acme");
        }

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void MakeCurrent_TenantWithTheKeyTypesDefaultId_ThrowsAndLeavesNoTenant()
    {
        var strings = BuildSetter();
        var guids = Setter<Guid>();
        var ints = Setter<int>();

        FluentActions.Invoking(() => strings.MakeCurrent(new TenantDescriptor<string> { TenantId = "", Name = "Empty" }))
            .Should().Throw<ArgumentException>().WithMessage("*reserves*\"no tenant\"*");
        FluentActions.Invoking(() => strings.MakeCurrent(new TenantDescriptor<string> { TenantId = null!, Name = "Null" }))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => guids.MakeCurrent(new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" }))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ints.MakeCurrent(new TenantDescriptor<int> { TenantId = 0, Name = "Zero" }))
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

    [Fact]
    public void MakeNoTenantCurrent_HidesTheTenant_UntilDisposed()
    {
        var setter = BuildSetter();

        using (setter.MakeCurrent(Tenant("acme")))
        {
            using (setter.MakeNoTenantCurrent())
            {
                setter.HasTenant.Should().BeFalse();
                setter.CurrentTenant.Should().BeNull();
                setter.CurrentTenantId.Should().BeNull();

                using (setter.MakeCurrent(Tenant("globex")))
                {
                    setter.CurrentTenantId.Should().Be("globex");
                }

                setter.HasTenant.Should().BeFalse();
            }

            setter.CurrentTenantId.Should().Be("acme");
        }

        setter.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void MakeNoTenantCurrent_ClosedOutOfOrder_LeavesTheInnermostScopeCurrent()
    {
        var setter = BuildSetter();

        using var outer = setter.MakeCurrent(Tenant("acme"));
        var none = setter.MakeNoTenantCurrent();
        var inner = setter.MakeCurrent(Tenant("globex"));

        none.Dispose();
        setter.CurrentTenantId.Should().Be("globex");

        inner.Dispose();
        setter.CurrentTenantId.Should().Be("acme");
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
