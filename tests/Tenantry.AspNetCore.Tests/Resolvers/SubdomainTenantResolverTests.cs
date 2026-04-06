using AwesomeAssertions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class SubdomainTenantResolverTests
{
    private static DefaultHttpContext ContextWithHost(string host)
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString(host);
        return context;
    }

    [Fact]
    public async Task MultiSegmentHost_ReturnsFirstSegment()
    {
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost("acme.app.example.com");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task SingleSegmentHost_ReturnsNull()
    {
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost("localhost");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TwoSegmentHost_ReturnsNull()
    {
        // "app.com" has only 2 segments — the resolver requires 3+ to distinguish
        // a true subdomain from a plain domain name.
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost("app.com");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubdomainPlusLocalhost_ReturnsNull()
    {
        // "acme.localhost" has only 2 segments — use header resolution for local dev.
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost("acme.localhost");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmptyHost_ReturnsNull()
    {
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost("");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }
}
