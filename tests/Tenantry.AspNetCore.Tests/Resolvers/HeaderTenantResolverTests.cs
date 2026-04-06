using AwesomeAssertions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class HeaderTenantResolverTests
{
    [Fact]
    public async Task Header_Present_ReturnsValue()
    {
        HeaderTenantResolver resolver = new("X-Tenant-Id");
        DefaultHttpContext context = new();
        context.Request.Headers["X-Tenant-Id"] = "acme";

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task Header_Absent_ReturnsNull()
    {
        HeaderTenantResolver resolver = new("X-Tenant-Id");
        DefaultHttpContext context = new();

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Header_WhitespaceOnly_ReturnsNull()
    {
        HeaderTenantResolver resolver = new("X-Tenant-Id");
        DefaultHttpContext context = new();
        context.Request.Headers["X-Tenant-Id"] = "   ";

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Header_WithLeadingTrailingSpaces_ReturnsTrimmedValue()
    {
        HeaderTenantResolver resolver = new("X-Tenant-Id");
        DefaultHttpContext context = new();
        context.Request.Headers["X-Tenant-Id"] = "  acme  ";

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task CustomHeaderName_IsUsed()
    {
        HeaderTenantResolver resolver = new("X-Custom-Tenant");
        DefaultHttpContext context = new();
        context.Request.Headers["X-Custom-Tenant"] = "globex";

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("globex");
    }
}
