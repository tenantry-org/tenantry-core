using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Tests the full TenantResolutionMiddleware pipeline via TestServer.
/// </summary>
public sealed class MiddlewareTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public MiddlewareTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
        });

        _app = builder.Build();
        _app.UseTenantry();
        _app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        _app.Start();
        _client = _app.GetTestClient();
    }

    [Fact]
    public async Task Request_WithValidTenantHeader_SetsTenantContext()
    {
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await _client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("acme");
    }

    [Fact]
    public async Task Request_WithUnknownTenantId_OnAnEndpointThatRequiresATenant_Returns404()
    {
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }]));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "unknown-corp");

        var response = await client.GetAsync("/tenant");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WithUnknownTenantId_OnAnOptionalEndpoint_ContinuesWithoutTenant()
    {
        using var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "unknown-corp");

        var response = await client.GetAsync("/tenant");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("(none)");
    }

    [Fact]
    public async Task Request_WithNoTenantHeader_ContinuesWithoutTenant()
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("(none)");
    }

    [Fact]
    public async Task Request_WithNoTenantHeader_AndTenantRequired_Returns400()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            tenant.RequireTenantByDefault();
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task Request_WithInvalidTenantIdFormat_Returns400()
    {
        // Use int as TKey so that "not-a-number" fails TryParse
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<int>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore([new TenantDescriptor<int> { TenantId = 1, Name = "Test" }]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<int> ctx) => ctx.CurrentTenantId.ToString());
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "not-a-number");

        var response = await client.GetAsync("/tenant");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ContextCleared_AfterRequestCompletes()
    {
        // Send two sequential requests: first with tenant header, second without.
        // The second request must NOT see the first request's tenant (AsyncLocal cleared in finally).
        using var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        await client.GetAsync("/tenant");

        using var client2 = _app.GetTestClient();
        var response = await client2.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be("(none)");
    }

    [Fact]
    public async Task MultipleResolvers_FirstReturnsValue_FirstIsUsed()
    {
        // Header resolver first (returns "acme"), query string second (would return "globex").
        // Only the first non-null result should be used.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.ResolveFromQueryString();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant?tenantId=globex");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be("acme");
    }

    [Fact]
    public async Task MultipleResolvers_FirstReturnsNull_SecondIsUsed()
    {
        // Header resolver first (no header → null), query string second (returns "acme").
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.ResolveFromQueryString();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();

        var response = await client.GetAsync("/tenant?tenantId=acme");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be("acme");
    }

    [Fact]
    public async Task AllResolversReturnNull_RequestContinuesWithoutTenant()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.ResolveFromQueryString();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();

        // No header, no query string
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("(none)");
    }

    [Fact]
    public async Task AllResolversReturnNull_AndTenantRequired_Returns400()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.ResolveFromQueryString();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            tenant.RequireTenantByDefault();
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task ValidateTenantAccess_WhenUserIsAllowed_SetsTenantContext()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccess((httpContext, tenantInfo) =>
                httpContext.User.HasClaim("tenant_id", tenantInfo.TenantId));
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "acme"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("acme");
    }

    [Fact]
    public async Task ValidateTenantAccess_WhenUserIsDenied_Returns403()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccess((httpContext, tenantInfo) =>
                httpContext.User.HasClaim("tenant_id", tenantInfo.TenantId));
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "acme"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task ValidateTenantAccess_WhenNamedUserIsDenied_Returns403()
    {
        // Identical to the denied case above, but the principal carries a Name claim and an
        // authentication type. This exercises the authenticated/named-user branch of the
        // access-denied warning log (User.Identity?.Name is non-null), complementing the
        // anonymous case which hits the "(anonymous)" fallback.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccess((httpContext, tenantInfo) =>
                httpContext.User.HasClaim("tenant_id", tenantInfo.TenantId));
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "alice"),
                new Claim("tenant_id", "acme"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task ValidateTenantAccess_WhenUnauthenticatedUserIsDenied_Returns403()
    {
        // The principal has no identity at all (User.Identity is null) and is denied —
        // exercises the null-identity branch of the access-denied warning log, which falls
        // back to "(anonymous)".
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            tenant.ValidateTenantAccess((_, _) => false);
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(); // no identities -> User.Identity is null
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task ValidateTenantAccessByClaim_WhenSingleClaimMatches_AllowsRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccessByClaim("tenant_id");
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "acme"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("acme");
    }

    [Fact]
    public async Task ValidateTenantAccessByClaim_WhenMultipleClaimsContainTenant_AllowsRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccessByClaim("tenant_id");
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "acme"),
                new Claim("tenant_id", "globex"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("globex");
    }

    [Fact]
    public async Task ValidateTenantAccessByClaim_WhenJsonArrayContainsParsedTenant_AllowsRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<int>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<int> { TenantId = 1, Name = "Acme Corp" },
                new TenantDescriptor<int> { TenantId = 2, Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccessByClaim("tenant_ids");
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_ids", "[1,2]"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<int> ctx) => ctx.CurrentTenantId.ToString());
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "2");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("2");
    }

    [Fact]
    public async Task ValidateTenantAccessByClaim_WhenNoClaimMatches_DeniesRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<int>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<int> { TenantId = 1, Name = "Acme Corp" },
                new TenantDescriptor<int> { TenantId = 2, Name = "Globex LLC" },
            ]);
            tenant.ValidateTenantAccessByClaim("tenant_ids");
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_ids", "[3,4]"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<int> ctx) => ctx.CurrentTenantId.ToString());
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "2");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task ValidateTenantAccess_WhenMultipleValidatorsConfigured_AllMustPass()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.RequireTenantByDefault();
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            tenant.ValidateTenantAccessByClaim("tenant_id");
            tenant.ValidateTenantAccess((httpContext, _) =>
                httpContext.User.HasClaim("tenant_access", "granted"));
        });

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "acme"),
            ], "test"));
            await next(context);
        });
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task Request_WithNoTenantHeader_AndEndpointRequiresTenant_Returns400()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)")
            .RequireTenant();
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        body.Should().BeEmpty("without IProblemDetailsService the rejection has no body");
    }

    [Fact]
    public async Task Request_WithGlobalRequirement_AndAllowMissingTenantEndpoint_ContinuesWithoutTenant()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            tenant.RequireTenantByDefault();
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)")
            .AllowMissingTenant();
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("(none)");
    }

    [Fact]
    public async Task EndpointLevelAllowMissingTenant_OverridesRequireTenantGroup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();

        var group = app.MapGroup("/group").RequireTenant();
        group.MapGet("/required", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        group.MapGet("/optional", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)")
            .AllowMissingTenant();

        await app.StartAsync();

        using var client = app.GetTestClient();

        var requiredResponse = await client.GetAsync("/group/required");
        requiredResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

        var optionalResponse = await client.GetAsync("/group/optional");
        var optionalBody = await optionalResponse.Content.ReadAsStringAsync();

        optionalResponse.IsSuccessStatusCode.Should().BeTrue();
        optionalBody.Should().Be("(none)");
    }

    [Fact]
    public async Task ControllerRequireTenantAttribute_WhenNoTenantResolved_Returns400()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TenantRequirementTestController).Assembly);

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapControllers();
        await app.StartAsync();

        using var client = app.GetTestClient();
        var requiredResponse = await client.GetAsync("/tenant-requirement/required");
        var optionalResponse = await client.GetAsync("/tenant-requirement/optional");
        var optionalBody = await optionalResponse.Content.ReadAsStringAsync();

        requiredResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        optionalResponse.IsSuccessStatusCode.Should().BeTrue();
        optionalBody.Should().Be("(none)");
    }

    [Fact]
    public async Task Middleware_WhenNoTenantAndNotRequired_WithDebugLogging_ContinuesWithoutTenant()
    {
        // Enables Debug logging so the IsEnabled(Debug) guard in the middleware is true,
        // covering the debug log branch at lines 85-90 of TenantResolutionMiddleware.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        // No tenant header — no tenant resolved, not required
        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("(none)");
    }

    [Fact]
    public async Task Middleware_WhenTenantResolved_WithDebugLogging_SetsTenantContext()
    {
        // Enables Debug logging so the IsEnabled(Debug) guard is true,
        // covering the debug log branch at lines 145-151 of TenantResolutionMiddleware.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/tenant");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("acme");
    }

    [Fact]
    public async Task Middleware_WhenNoEndpointMatched_UsesDefaultTenantRequirement()
    {
        // A request to an unmapped path has no endpoint — context.GetEndpoint() returns null.
        // This exercises the null-endpoint branch in IsTenantRequired (line 165-167).
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.ResolveFromHeader("X-Tenant-Id");
            tenant.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            // RequireTenantByDefault = false (default), so middleware lets the request through
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var client = app.GetTestClient();
        // Request to an unmapped path — no endpoint, no tenant header
        var response = await client.GetAsync("/unmapped-path-that-has-no-endpoint");

        // Middleware defers to default (not required), framework returns 404
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    public async Task Request_WithAnInvalidOrDefaultId_OnAnOptionalEndpoint_ContinuesWithoutTenant(string identifier)
    {
        await using var app = await StartAsync<int>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([new TenantDescriptor<int> { TenantId = 1, Name = "Test" }]));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", identifier);

        var response = await client.GetAsync("/tenant");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("(none)");
    }

    [Fact]
    public async Task Request_WithTheKeyTypesDefaultId_OnAnEndpointThatRequiresATenant_Returns400()
    {
        // A tenant stored with the default id is never served: Tenantry reserves it for "no tenant".
        await using var app = await StartAsync<Guid>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" }]));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.Empty.ToString());

        var response = await client.GetAsync("/tenant");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnEmptyIdentifierFromACustomResolver_IsInvalid()
    {
        // The built-in resolvers never return "", but a custom one can: like Guid.Empty, it cannot be a tenant id.
        await using var app = await StartAsync<string>(tenant => tenant
            .UseResolver(new EmptyResolver())
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }]));
        using var client = app.GetTestClient();

        (await client.GetAsync("/tenant")).StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private sealed class EmptyResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("");
    }

    [Fact]
    public async Task Rejections_WithProblemDetails_AreProblemDetailsThatNeverRepeatTheIdentifier()
    {
        const string identifier = "<script>alert(1)</script>";
        await using var app = await StartAsync<int>(
            tenant => tenant
                .ResolveFromHeader("X-Tenant-Id")
                .RequireTenantByDefault()
                .UseInMemoryStore([new TenantDescriptor<int> { TenantId = 1, Name = "Test" }]),
            services => services.AddProblemDetails());

        foreach (var (header, status, title) in new[]
                 {
                     ((string?)null, 400, "Tenant required"),
                     (identifier, 400, "Invalid tenant"),
                     ("31337", 404, "Tenant not found"),
                 })
        {
            using var client = app.GetTestClient();
            if (header is not null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Tenant-Id", header);
            }

            var response = await client.GetAsync("/tenant");
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().Be(status);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            var problem = System.Text.Json.JsonDocument.Parse(body).RootElement;
            problem.GetProperty("status").GetInt32().Should().Be(status);
            problem.GetProperty("title").GetString().Should().Be(title);
            if (header is not null)
            {
                System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "")
                    .Should().NotContain(header).And.NotContain("script");
            }
        }
    }

    [Fact]
    public async Task WithAccessValidators_AnUnknownTenant_GetsTheSameResponseAsADeniedOne()
    {
        await using var app = await StartAsync<string>(
            tenant => tenant
                .ResolveFromHeader("X-Tenant-Id")
                .RequireTenantByDefault()
                .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }])
                .ValidateTenantAccess((_, _) => false),
            services => services.AddProblemDetails());

        async Task<(System.Net.HttpStatusCode Status, string Body)> Get(string tenantId)
        {
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
            var response = await client.GetAsync("/tenant");
            var body = await response.Content.ReadAsStringAsync();

            // Every response has its own trace id.
            return (response.StatusCode, System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", ""));
        }

        var denied = await Get("acme");
        var unknown = await Get("unknown-corp");

        denied.Status.Should().Be(System.Net.HttpStatusCode.Forbidden);
        unknown.Should().Be(denied, "a caller must not learn which tenants exist");
    }

    [Fact]
    public async Task WithAccessValidators_OnAnOptionalEndpoint_DeniedAndUnknownTenants_BothContinueWithoutTenant()
    {
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }])
            .ValidateTenantAccess((_, _) => false));

        foreach (var tenantId in new[] { "acme", "unknown-corp" })
        {
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

            var response = await client.GetAsync("/tenant");

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("(none)");
        }
    }

    [Fact]
    public async Task ConfiguredStatusCodes_AreUsed()
    {
        await using var app = await StartAsync<int>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<int> { TenantId = 1, Name = "Test" }])
            .ConfigureResolution(options =>
            {
                options.MissingTenantStatusCode = 401;
                options.InvalidTenantStatusCode = 422;
                options.TenantNotFoundStatusCode = 410;
            }));

        foreach (var (header, status) in new[] { ((string?)null, 401), ("x", 422), ("2", 410) })
        {
            using var client = app.GetTestClient();
            if (header is not null)
            {
                client.DefaultRequestHeaders.Add("X-Tenant-Id", header);
            }

            ((int)(await client.GetAsync("/tenant")).StatusCode).Should().Be(status);
        }
    }

    [Fact]
    public async Task ConfiguredAccessDeniedStatusCode_IsUsedForDeniedAndUnknownTenants()
    {
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .RequireTenantByDefault()
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" }])
            .ValidateTenantAccess((_, _) => false)
            .ConfigureResolution(options => options.AccessDeniedStatusCode = 404));

        foreach (var tenantId in new[] { "acme", "unknown-corp" })
        {
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

            (await client.GetAsync("/tenant")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
    }

    // Starts an application with Tenantry configured by configure, and GET /tenant returning the current tenant id.
    private static async Task<WebApplication> StartAsync<TKey>(
        Action<ITenantBuilder<TKey>> configure,
        Action<IServiceCollection>? services = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry(configure);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<TKey> ctx) => ctx.HasTenant ? ctx.CurrentTenantId!.ToString()! : "(none)");
        await app.StartAsync();
        return app;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}

[ApiController]
[Route("tenant-requirement")]
public sealed class TenantRequirementTestController : ControllerBase
{
    [HttpGet("required")]
    [RequireTenant]
    public ActionResult<string> GetRequired(ITenantContext<string> tenantContext) =>
        tenantContext.CurrentTenantId ?? "(none)";

    [HttpGet("optional")]
    [AllowMissingTenant]
    public ActionResult<string> GetOptional(ITenantContext<string> tenantContext) =>
        tenantContext.CurrentTenantId ?? "(none)";
}
