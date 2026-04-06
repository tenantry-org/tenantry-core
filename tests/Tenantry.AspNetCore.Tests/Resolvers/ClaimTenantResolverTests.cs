using System.Security.Claims;
using AwesomeAssertions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class ClaimTenantResolverTests
{
    private static DefaultHttpContext ContextWithClaim(string claimType, string claimValue, string? authenticationType = "test")
    {
        DefaultHttpContext context = new();
        ClaimsIdentity identity = new(
        [
            new Claim(claimType, claimValue),
        ],
        authenticationType: authenticationType);
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private static DefaultHttpContext ContextWithoutClaim(string? authenticationType = "test")
    {
        DefaultHttpContext context = new();
        ClaimsIdentity identity = new(authenticationType: authenticationType);
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    [Fact]
    public async Task AuthenticatedUser_WithClaim_ReturnsClaimValue()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", "acme");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task AuthenticatedUser_WithoutClaim_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithoutClaim();

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_WithClaim_ReturnsClaimValue()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", "acme", authenticationType: null);

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_WithoutClaim_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithoutClaim(authenticationType: null);

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Claim_WhitespaceOnly_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", "   ");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CustomClaimType_IsUsed()
    {
        ClaimTenantResolver resolver = new("org_id");
        var context = ContextWithClaim("org_id", "globex");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("globex");
    }
}
