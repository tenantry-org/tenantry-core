using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Extensions;

namespace Tenantry.Core.Tests;

public sealed class TenantScopeTests
{
    private static ITenantScope<string> BuildScope()
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>();
        return services.BuildServiceProvider().GetRequiredService<ITenantScope<string>>();
    }

    [Fact]
    public void BeginScope_SetsCurrentTenantAndHasTenant()
    {
        var scope = BuildScope();
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        using (scope.BeginScope(descriptor))
        {
            scope.HasTenant.Should().BeTrue();
            scope.CurrentTenant.Should().Be(descriptor);
        }

        scope.HasTenant.Should().BeFalse();
        scope.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void BeginScope_WhenNested_ShadowsOuterThenRestoresOnDispose()
    {
        var scope = BuildScope();
        TenantDescriptor<string> outer = new() { TenantId = "acme", Name = "Acme Corp" };
        TenantDescriptor<string> inner = new() { TenantId = "globex", Name = "Globex" };

        using (scope.BeginScope(outer))
        {
            scope.CurrentTenant.Should().Be(outer);

            using (scope.BeginScope(inner))
            {
                scope.CurrentTenant.Should().Be(inner, "the inner scope shadows the outer tenant");
            }

            scope.CurrentTenant.Should().Be(outer, "disposing the inner scope restores the outer tenant");
        }

        scope.HasTenant.Should().BeFalse("disposing the outermost scope restores 'no tenant'");
        scope.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotDisturbALaterScope()
    {
        var scope = BuildScope();
        var first = scope.BeginScope(Tenant("acme"));
        first.Dispose();

        using (scope.BeginScope(Tenant("globex")))
        {
            first.Dispose();

            scope.CurrentTenantId.Should().Be("globex", "a second dispose of a closed scope does nothing");
        }

        scope.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void Dispose_OutOfOrder_KeepsTheInnerScopeAndThenRestoresNoTenant()
    {
        var scope = BuildScope();
        var outer = scope.BeginScope(Tenant("acme"));
        var inner = scope.BeginScope(Tenant("globex"));

        outer.Dispose();
        scope.CurrentTenantId.Should().Be("globex", "closing the outer scope first leaves the inner one active");

        inner.Dispose();
        scope.HasTenant.Should().BeFalse("the outer scope is already closed, so it is not restored");
    }

    [Fact]
    public void Dispose_MiddleScopeFirst_RestoresTheNearestOpenScope()
    {
        var scope = BuildScope();
        using var outer = scope.BeginScope(Tenant("acme"));
        var middle = scope.BeginScope(Tenant("globex"));
        var inner = scope.BeginScope(Tenant("initech"));

        middle.Dispose();
        inner.Dispose();

        scope.CurrentTenantId.Should().Be("acme");
    }

    [Fact]
    public async Task Dispose_FromAnotherAsyncFlow_DoesNotChangeTheCallersTenant()
    {
        var scope = BuildScope();

        using (scope.BeginScope(Tenant("acme")))
        {
            // The scope begins inside Task.Run, so it is never visible to this flow.
            var handle = await Task.Run(() => scope.BeginScope(Tenant("globex")));

            handle.Dispose();

            scope.CurrentTenantId.Should().Be("acme");
        }

        scope.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_ByAChildFlowFirst_StillRestoresTheCallerWhenTheCallerDisposes()
    {
        var scope = BuildScope();

        using (scope.BeginScope(Tenant("old")))
        {
            var handle = scope.BeginScope(Tenant("acme"));

            // The child flow inherits the scope and closes it. Its restore only affects the child's own flow.
            await Task.Run(handle.Dispose);
            scope.CurrentTenantId.Should().Be("acme", "a child flow cannot change the caller's ambient tenant");

            handle.Dispose();
            scope.CurrentTenantId.Should().Be("old", "the caller's own disposal still restores its previous tenant");
        }

        scope.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task UsingBlock_WhoseHandleAChildFlowDisposed_RestoresNoTenant()
    {
        var scope = BuildScope();

        using (var handle = scope.BeginScope(Tenant("acme")))
        {
            await Task.Run(handle.Dispose);
        }

        scope.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_OfAnOuterScopeByAChildFlow_IsSkippedWhenTheCallersInnerScopeCloses()
    {
        var scope = BuildScope();
        var outer = scope.BeginScope(Tenant("acme"));
        var inner = scope.BeginScope(Tenant("globex"));

        await Task.Run(outer.Dispose);
        scope.CurrentTenantId.Should().Be("globex");

        inner.Dispose();
        scope.HasTenant.Should().BeFalse("the outer scope was already closed, so it is not restored");

        outer.Dispose();
        scope.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_OfTheInnerScopeByAChildFlow_ThenByTheCaller_RestoresTheOuterScope()
    {
        var scope = BuildScope();

        using (scope.BeginScope(Tenant("acme")))
        {
            var inner = scope.BeginScope(Tenant("globex"));
            await Task.Run(inner.Dispose);

            inner.Dispose();

            scope.CurrentTenantId.Should().Be("acme");
        }

        scope.HasTenant.Should().BeFalse();
    }

    private static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };
}
