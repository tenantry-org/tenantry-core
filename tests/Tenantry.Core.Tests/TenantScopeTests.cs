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
}
