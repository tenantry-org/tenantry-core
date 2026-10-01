using AwesomeAssertions;

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
    public async Task SubdomainPlusLocalhost_WithoutABaseDomain_ReturnsNull()
    {
        // "acme.localhost" has only 2 segments; a base domain of "localhost" resolves it (below).
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

    [Fact]
    public async Task MultiSegmentHost_WithEmptyLeadingSegment_ReturnsNull()
    {
        // ".example.com" has 3 segments (so it passes the length check), but the first
        // label is blank — the resolved subdomain is whitespace, so we return null
        // rather than an empty string.
        SubdomainTenantResolver resolver = new();
        var context = ContextWithHost(".example.com");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("www.example.com")]
    [InlineData("WWW.example.com")]
    public async Task Www_IsIgnoredByDefault(string host)
    {
        (await new SubdomainTenantResolver().ResolveAsync(ContextWithHost(host))).Should().BeNull();
    }

    [Fact]
    public async Task IgnoredSubdomains_CanBeAdded_AndWwwRemoved()
    {
        SubdomainTenantResolverOptions options = new();
        options.IgnoredSubdomains.Add("api");
        options.IgnoredSubdomains.Remove("www");
        SubdomainTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost("api.example.com"))).Should().BeNull();
        (await resolver.ResolveAsync(ContextWithHost("www.example.com"))).Should().Be("www");
    }

    [Fact]
    public async Task Options_AreCopiedWhenTheResolverIsCreated()
    {
        SubdomainTenantResolverOptions options = new();
        SubdomainTenantResolver resolver = new(options);

        options.IgnoredSubdomains.Add("acme");
        options.BaseDomains.Add("other.org");

        (await resolver.ResolveAsync(ContextWithHost("acme.example.com"))).Should().Be("acme");
    }

    [Theory]
    [InlineData("10.0.0.12")]
    [InlineData("192.168.1.1")]
    [InlineData("[::1]")]
    public async Task IpAddressHost_ReturnsNull(string host)
    {
        // Load balancers and Kubernetes probes address a pod by IP.
        (await new SubdomainTenantResolver().ResolveAsync(ContextWithHost(host))).Should().BeNull();
    }

    [Theory]
    [InlineData("acme.example.com", "acme")]
    [InlineData("ACME.Example.COM", "acme")]
    [InlineData("acme.example.com.", "acme")]
    [InlineData("example.com", null)]
    [InlineData("www.example.com", null)]
    [InlineData("x.acme.example.com", null)]
    [InlineData("acme.other.org", null)]
    [InlineData("acme.notexample.com", null)]
    [InlineData("acme.example.com.evil.org", null)]
    public async Task BaseDomain_OnlyAHostOfOneLabelUnderIt_ResolvesATenant(string host, string? expected)
    {
        SubdomainTenantResolverOptions options = new();
        options.BaseDomains.Add("example.com");
        SubdomainTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost(host))).Should().Be(expected);
    }

    [Fact]
    public async Task BaseDomain_Localhost_ResolvesSubdomainsInDevelopment()
    {
        SubdomainTenantResolverOptions options = new();
        options.BaseDomains.Add(".localhost.");
        SubdomainTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost("acme.localhost"))).Should().Be("acme");
        (await resolver.ResolveAsync(ContextWithHost("localhost"))).Should().BeNull();
    }

    [Theory]
    [InlineData("münchen.de")]
    [InlineData("xn--mnchen-3ya.de")]
    public async Task AnInternationalBaseDomain_MatchesInEitherForm_AndTheSubdomainIsAscii(string baseDomain)
    {
        SubdomainTenantResolverOptions options = new();
        options.BaseDomains.Add(baseDomain);
        SubdomainTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost("acme.münchen.de"))).Should().Be("acme");
        (await resolver.ResolveAsync(ContextWithHost("acme.xn--mnchen-3ya.de"))).Should().Be("acme");
        (await resolver.ResolveAsync(ContextWithHost("bücher.münchen.de"))).Should().Be("xn--bcher-kva");

        options.IgnoredSubdomains.Add("Bücher");
        (await new SubdomainTenantResolver(options).ResolveAsync(ContextWithHost("bücher.münchen.de"))).Should().BeNull();
        (await new SubdomainTenantResolver(options).ResolveAsync(ContextWithHost("xn--bcher-kva.münchen.de"))).Should().BeNull();
    }

    [Theory]
    [InlineData("acme.example.com", "acme")]
    [InlineData("globex.example.co.uk", "globex")]
    [InlineData("initech.localhost", "initech")]
    [InlineData("acme.app.example.com", "acme")]
    [InlineData("app.example.com", "app")]
    [InlineData("acme.other.org", null)]
    [InlineData("example.co.uk", null)]
    public async Task BaseDomains_EachResolvesItsSubdomains(string host, string? expected)
    {
        SubdomainTenantResolverOptions options = new();
        options.BaseDomains.Add("example.com");
        options.BaseDomains.Add("example.co.uk");
        options.BaseDomains.Add("app.example.com");
        options.BaseDomains.Add("localhost");
        options.BaseDomains.Add(" ");
        SubdomainTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost(host))).Should().Be(expected);
    }
}
