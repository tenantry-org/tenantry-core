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
    public void BeginScope_WhenScopeAlreadyActive_Throws()
    {
        var scope = BuildScope();
        TenantDescriptor<string> descriptor = new() { TenantId = "acme", Name = "Acme Corp" };

        using var _ = scope.BeginScope(descriptor);

        var act = () => scope.BeginScope(descriptor);
        act.Should().Throw<InvalidOperationException>();
    }
}
