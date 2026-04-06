using AwesomeAssertions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class RouteValueTenantResolverTests
{
    private static DefaultHttpContext ContextWithRouteValue(string key, object? value)
    {
        DefaultHttpContext context = new();
        context.Request.RouteValues[key] = value;
        return context;
    }

    [Fact]
    public async Task RouteValue_Present_ReturnsValue()
    {
        RouteValueTenantResolver resolver = new();
        var context = ContextWithRouteValue("tenant", "acme");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task RouteValue_Absent_ReturnsNull()
    {
        RouteValueTenantResolver resolver = new();
        DefaultHttpContext context = new();

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RouteValue_WhitespaceOnly_ReturnsNull()
    {
        RouteValueTenantResolver resolver = new();
        var context = ContextWithRouteValue("tenant", "   ");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CustomRouteKey_IsUsed()
    {
        RouteValueTenantResolver resolver = new("org");
        var context = ContextWithRouteValue("org", "globex");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("globex");
    }
}
