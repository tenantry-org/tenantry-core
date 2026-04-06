using AwesomeAssertions;
using Tenantry.AspNetCore.Resolution;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class QueryStringTenantResolverTests
{
    [Fact]
    public async Task Parameter_Present_ReturnsValue()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=acme");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task Parameter_Absent_ReturnsNull()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Parameter_WhitespaceOnly_ReturnsNull()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=   ");

        var result = await resolver.ResolveAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Parameter_WithSpaces_ReturnsTrimmedValue()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=+acme+");

        var result = await resolver.ResolveAsync(context);

        // URL-decoded " acme " trimmed → "acme"
        result.Should().Be("acme");
    }

    [Fact]
    public async Task CustomParameterName_IsUsed()
    {
        QueryStringTenantResolver resolver = new("tid");
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tid=globex");

        var result = await resolver.ResolveAsync(context);

        result.Should().Be("globex");
    }
}
