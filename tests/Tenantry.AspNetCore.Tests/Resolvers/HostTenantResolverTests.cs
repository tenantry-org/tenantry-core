using AwesomeAssertions;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class HostTenantResolverTests
{
    private static DefaultHttpContext ContextWithHost(string host)
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString(host);
        return context;
    }

    [Theory]
    [InlineData("app.acme.com", "app.acme.com")]
    [InlineData("App.ACME.com", "app.acme.com")]
    [InlineData("app.acme.com.", "app.acme.com")]
    [InlineData("app.acme.com:8443", "app.acme.com")]
    [InlineData("", null)]
    [InlineData("10.0.0.12", null)]
    [InlineData("[::1]:5000", null)]
    [InlineData("localhost", null)]
    [InlineData("acme.localhost", null)]
    [InlineData("notlocalhost", "notlocalhost")]
    public async Task ResolvesTheHostName_InLowerCase_WithoutPortOrTrailingDot_ButNotLocalhost(string host, string? expected)
    {
        (await new HostTenantResolver().ResolveAsync(ContextWithHost(host))).Should().Be(expected);
    }

    [Theory]
    [InlineData("example.com", null)]
    [InlineData("www.example.com", null)]
    [InlineData("x.acme.example.com", null)]
    [InlineData("EXAMPLE.com.", null)]
    [InlineData("notexample.com", "notexample.com")]
    [InlineData("example.com.evil.org", "example.com.evil.org")]
    [InlineData("app.acme.com", "app.acme.com")]
    [InlineData("localhost", "localhost")]
    public async Task ExcludedDomains_AndTheirSubdomains_ResolveNothing(string host, string? expected)
    {
        HostTenantResolverOptions options = new();
        options.ExcludedDomains.Remove("localhost");
        options.ExcludedDomains.Add(".Example.com ");
        HostTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost(host))).Should().Be(expected);
    }

    [Theory]
    [InlineData("app.münchen.de")]
    [InlineData("app.xn--mnchen-3ya.de")]
    public async Task AnInternationalDomainName_ResolvesInItsAsciiForm(string host)
    {
        (await new HostTenantResolver().ResolveAsync(ContextWithHost(host))).Should().Be("app.xn--mnchen-3ya.de");
    }

    [Theory]
    [InlineData("münchen.de")]
    [InlineData("xn--mnchen-3ya.de")]
    public async Task AnExcludedInternationalDomain_MatchesInEitherForm(string excluded)
    {
        HostTenantResolverOptions options = new();
        options.ExcludedDomains.Add(excluded);
        HostTenantResolver resolver = new(options);

        (await resolver.ResolveAsync(ContextWithHost("www.münchen.de"))).Should().BeNull();
        (await resolver.ResolveAsync(ContextWithHost("www.xn--mnchen-3ya.de"))).Should().BeNull();
    }

    [Fact]
    public async Task Options_AreCopiedWhenTheResolverIsCreated()
    {
        HostTenantResolverOptions options = new();
        HostTenantResolver resolver = new(options);

        options.ExcludedDomains.Add("acme.com");

        (await resolver.ResolveAsync(ContextWithHost("app.acme.com"))).Should().Be("app.acme.com");
    }
}
