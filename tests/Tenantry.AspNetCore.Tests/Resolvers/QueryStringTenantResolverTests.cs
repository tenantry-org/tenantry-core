using AwesomeAssertions;

namespace Tenantry.AspNetCore.Tests.Resolvers;

public sealed class QueryStringTenantResolverTests
{
    [Fact]
    public async Task Parameter_GivenTwice_ReturnsNull()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new("?tenantId=acme&tenantId=globex");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Parameter_Present_ReturnsValue()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=acme");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task Parameter_Absent_ReturnsNull()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Parameter_WhitespaceOnly_ReturnsNull()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=   ");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Parameter_WithSpaces_ReturnsTrimmedValue()
    {
        QueryStringTenantResolver resolver = new();
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tenantId=+acme+");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        // URL-decoded " acme " trimmed → "acme"
        result.Should().Be("acme");
    }

    [Fact]
    public async Task CustomParameterName_IsUsed()
    {
        QueryStringTenantResolver resolver = new("tid");
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tid=globex");

        var result = await resolver.ResolveAsync(context, TestContext.Current.CancellationToken);

        result.Should().Be("globex");
    }
}
