using System.Security.Claims;
using AwesomeAssertions;

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

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task AuthenticatedUser_WithoutClaim_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithoutClaim();

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_WithClaim_ReturnsClaimValue()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", "acme", authenticationType: null);

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_WithoutClaim_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithoutClaim(authenticationType: null);

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Claim_WhitespaceOnly_ReturnsNull()
    {
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", "   ");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TwoClaimsOfTheType_ReturnNull_AndOneAmongOtherClaimsIsReturned()
    {
        // A token that lists the tenants a caller may use names none of them as the request's tenant.
        ClaimTenantResolver resolver = new();
        DefaultHttpContext several = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "acme"), new Claim("tenant_id", "globex")], "test")),
        };
        DefaultHttpContext one = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "acme"), new Claim("role", "admin")], "test")),
        };

        (await resolver.ResolveAsync(several, TestContext.Current.CancellationToken)).Should().BeNull();
        (await resolver.ResolveAsync(one, TestContext.Current.CancellationToken)).Should().Be("acme");
    }

    [Theory]
    [InlineData("[\"acme\"]", "acme")]
    [InlineData(" [ \" acme \" ] ", "acme")]
    [InlineData("[7]", "7")]
    [InlineData("[\"acme\",\"globex\"]", null)]
    [InlineData("[]", null)]
    [InlineData("[\"acme\"", "[\"acme\"")]
    public async Task OneClaimHoldingAJsonArray_IsReadAsTheTenantsItLists(string claimValue, string? expected)
    {
        // As ValidateTenantAccessByClaim reads it: one tenant resolves, several resolve none, and text that is not a
        // JSON array is one identifier.
        ClaimTenantResolver resolver = new();
        var context = ContextWithClaim("tenant_id", claimValue);

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task CustomClaimType_IsUsed()
    {
        ClaimTenantResolver resolver = new("org_id");
        var context = ContextWithClaim("org_id", "globex");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be("globex");
    }
}
