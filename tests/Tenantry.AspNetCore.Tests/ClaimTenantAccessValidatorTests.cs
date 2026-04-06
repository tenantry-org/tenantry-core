using System.Security.Claims;
using AwesomeAssertions;
using Tenantry.AspNetCore.Internal;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Tests;

public sealed class ClaimTenantAccessValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithDirectClaimMatch_ReturnsTrue()
    {
        var context = CreateContext(new Claim("tenant_id", "acme"));
        TenantDescriptor<string> tenant = new() { TenantId = "acme", Name = "Acme Corp" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_id");

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithJsonArrayStringClaim_ReturnsTrue()
    {
        var context = CreateContext(new Claim("tenant_ids", "[\"acme\",\"globex\"]"));
        TenantDescriptor<string> tenant = new() { TenantId = "globex", Name = "Globex LLC" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_ids");

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithMixedJsonArray_IgnoresUnsupportedElementsAndMatchesNumber()
    {
        var context = CreateContext(new Claim("tenant_ids", "[true, null, {}, \"  \", 2]"));
        TenantDescriptor<int> tenant = new() { TenantId = 2, Name = "Globex LLC" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_ids");

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithMalformedJsonArray_ReturnsFalse()
    {
        var context = CreateContext(new Claim("tenant_ids", "[\"acme\","));
        TenantDescriptor<string> tenant = new() { TenantId = "acme", Name = "Acme Corp" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_ids");

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithJsonObjectClaim_ReturnsFalse()
    {
        var context = CreateContext(new Claim("tenant_ids", "{\"tenant\":\"acme\"}"));
        TenantDescriptor<string> tenant = new() { TenantId = "acme", Name = "Acme Corp" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_ids");

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithJsonArrayContainingNoMatch_ReturnsFalse()
    {
        // Valid JSON array, but none of the values match the tenant ID.
        // This exercises the return-false path after the foreach loop in ClaimMatchesTenant.
        var context = CreateContext(new Claim("tenant_ids", "[\"other1\",\"other2\"]"));
        TenantDescriptor<string> tenant = new() { TenantId = "acme", Name = "Acme Corp" };

        var allowed = await ClaimTenantAccessValidator.ValidateAsync(context, tenant, "tenant_ids");

        allowed.Should().BeFalse();
    }

    private static DefaultHttpContext CreateContext(params Claim[] claims)
    {
        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"))
        };

        return context;
    }
}