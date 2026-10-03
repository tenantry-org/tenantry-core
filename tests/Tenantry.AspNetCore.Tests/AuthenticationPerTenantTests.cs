using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Authentication with each tenant's settings: <c>app.UseTenantResolution()</c> makes the tenant current before the
/// authentication middleware, which reads the scheme's options per tenant (<c>ConfigurePerTenant</c>), and
/// <c>app.UseTenantry()</c> runs the access validators after it.
/// </summary>
public sealed class AuthenticationPerTenantTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private static readonly Dictionary<string, SymmetricSecurityKey> Keys = new()
    {
        ["acme"] = Key('a'),
        ["globex"] = Key('g'),
        ["default"] = Key('d'),
    };

    [Fact]
    public async Task EachTenant_ValidatesTokensWithItsOwnIssuerAndKey()
    {
        await using var app = await StartJwtAsync();

        (await Get(app, "acme", "/whoami", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme:alice"));
        (await Get(app, "globex", "/whoami", Token("globex", "bob"))).Should().Be((HttpStatusCode.OK, "globex:bob"));

        // Acme's token on Globex: Globex's issuer and key refuse it.
        (await Get(app, "globex", "/whoami", Token("acme", "alice"))).Status.Should().Be(HttpStatusCode.Unauthorized);

        // The default settings never apply to a tenant's request.
        (await Get(app, "acme", "/whoami", Token("default", "mallory"))).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATenantTheValidatorsRefuse_IsNotCurrentForTheRestOfTheRequest()
    {
        await using var app = await StartJwtAsync(tenant => tenant.ValidateTenantAccess((_, t) => t.TenantId != "globex"));

        (await Get(app, "globex", "/required", Token("globex", "bob"))).Status.Should().Be(HttpStatusCode.Forbidden);
        (await Get(app, "globex", "/tenant", Token("globex", "bob"))).Should().Be((HttpStatusCode.OK, "(none)"));
        (await Get(app, "acme", "/tenant", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme"));
    }

    [Fact]
    public async Task WithNothingToResolveBeforeAuthentication_UseTenantryTriesTheClaimResolvers()
    {
        await using var app = await StartJwtAsync(tenant => tenant.ResolveFromClaim("tenant_id"));

        // No subdomain: the default settings authenticate, then the claim names the tenant.
        (await Get(app, null, "/required", Token("default", "carol", tenantClaim: "acme"))).Should().Be((HttpStatusCode.OK, "acme"));
    }

    [Fact]
    public async Task AnEndpoint_UseTenantryDidNotRunFor_DoesNotRun()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartAsync(
            tenant => tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("example.com")).UseInMemoryStore([Acme]),
            services: null,
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseWhen(c => !c.Request.Path.StartsWithSegments("/skipped"), b => b.UseTenantry());
            },
            logs);

        (await Get(app, "acme", "/tenant")).Should().Be((HttpStatusCode.OK, "acme"));
        (await Get(app, "acme", "/skipped")).Status.Should().Be(HttpStatusCode.InternalServerError);
        logs.For(1011).Should().ContainSingle().Which.Message.Should().Contain("/skipped");
    }

    [Fact]
    public async Task UseTenantResolution_WithoutUseTenantry_FailsTheApplicationsStart()
    {
        var start = () => StartAsync(
            tenant => tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("example.com")).UseInMemoryStore([Acme]),
            services: null,
            pipeline: a => a.UseTenantResolution());

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseTenantResolution()*UseTenantry()*");
    }

    [Fact]
    public async Task UseTenantResolution_AfterAuthentication_IsLoggedOnce()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartAsync(
            JwtTenants,
            AddJwt,
            pipeline: a =>
            {
                a.UseAuthentication();
                a.UseTenantResolution();
                a.UseTenantry();
            },
            logs);

        await Get(app, "acme", "/tenant", Token("acme", "alice"));
        await Get(app, "acme", "/tenant", Token("acme", "alice"));

        logs.For(1010).Should().ContainSingle();
    }

    [Fact]
    public async Task ACookieIssuedForOneTenant_IsRefusedOnAnother_ByTheClaimValidator()
    {
        await using var app = await StartAsync(
            tenant => tenant
                .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
                .UseInMemoryStore([Acme, Globex])
                .ValidateTenantAccessByClaim("tenant_id")
                .ConfigurePerTenant<CookieAuthenticationOptions>(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    (o, t) => o.Cookie.Name = $"auth-{t.Name.ToLowerInvariant()}"),
            services => services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(),
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                // The anonymous caller is not entitled to the tenant yet, so the validator leaves it not current: a
                // sign-in endpoint names the tenant itself (here, from the host). Its handler was created by the
                // authentication middleware with the tenant current, so it writes the tenant's cookie.
                a.MapGet("/sign-in", async (HttpContext http, ITenantContext<string> tenant) =>
                {
                    tenant.HasTenant.Should().BeFalse();
                    var signingInTo = http.Request.Host.Host.Split('.')[0];
                    ClaimsIdentity identity = new([new Claim("tenant_id", signingInTo)], CookieAuthenticationDefaults.AuthenticationScheme);
                    await http.SignInAsync(new ClaimsPrincipal(identity));
                }).AllowMissingTenant();
            });
        using var client = app.GetTestClient();

        // Acme's sign-in sets Acme's cookie, which Acme accepts.
        var signIn = await Send(client, "acme", "/sign-in");
        var cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("auth-acme=", StringComparison.Ordinal));
        var value = cookie.Split(';')[0]["auth-acme=".Length..];

        (await Send(client, "acme", "/required", cookie: $"auth-acme={value}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The same ticket under Globex's cookie name decrypts there (one key ring), but names Acme: refused.
        (await Send(client, "globex", "/required", cookie: $"auth-globex={value}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static void JwtTenants(ITenantBuilder<string> tenant) =>
        tenant
            .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
            .UseInMemoryStore([Acme, Globex])
            .ConfigurePerTenant<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (o, t) =>
            {
                o.TokenValidationParameters.ValidIssuer = Issuer(t.Name.ToLowerInvariant());
                o.TokenValidationParameters.IssuerSigningKey = Keys[t.Name.ToLowerInvariant()];
            });

    private static void AddJwt(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer("default"),
                ValidAudience = "api",
                IssuerSigningKey = Keys["default"],
                NameClaimType = "sub",
            };
        });
        services.AddAuthorization();
    }

    private static Task<WebApplication> StartJwtAsync(Action<ITenantBuilder<string>>? more = null) =>
        StartAsync(
            tenant =>
            {
                JwtTenants(tenant);
                more?.Invoke(tenant);
            },
            AddJwt,
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/whoami", (HttpContext http, ITenantContext<string> tenant) => $"{tenant.CurrentTenantId}:{http.User.Identity!.Name}")
                    .RequireAuthorization()
                    .RequireTenant();
            });

    private static async Task<WebApplication> StartAsync(
        Action<ITenantBuilder<string>> configure,
        Action<IServiceCollection>? services,
        Action<WebApplication> pipeline,
        RecordingLoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });

        if (logs is not null)
        {
            builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        }

        builder.Services.AddTenantry(configure);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        pipeline(app);

        static string Current(ITenantContext<string> context) => context.CurrentTenantId ?? "(none)";

        app.MapGet("/tenant", (ITenantContext<string> context) => Current(context));
        app.MapGet("/required", (ITenantContext<string> context) => Current(context)).RequireTenant();
        app.MapGet("/skipped", (ITenantContext<string> context) => Current(context));

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(WebApplication app, string? tenant, string path, string? token = null)
    {
        using var client = app.GetTestClient();
        using var response = await Send(client, tenant, path, token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string? tenant, string path, string? token = null, string? cookie = null)
    {
        var host = tenant is null ? "localhost" : $"{tenant}.example.com";
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://{host}{path}");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string issuer, string user, string? tenantClaim = null)
    {
        Dictionary<string, object> claims = new() { ["sub"] = user };

        if (tenantClaim is not null)
        {
            claims["tenant_id"] = tenantClaim;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer(issuer),
            Audience = "api",
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Keys[issuer], SecurityAlgorithms.HmacSha256),
        });
    }

    private static string Issuer(string name) => $"https://{name}.idp.example";

    private static SymmetricSecurityKey Key(char fill) => new(Encoding.UTF8.GetBytes(new string(fill, 32)));
}
