using System.Net;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Tenantry.IntegrationTests.Providers;

namespace Tenantry.IntegrationTests;

/// <summary>
/// ASP.NET Core Identity's cookie, checked on every request (a security stamp interval of zero), behind
/// <c>app.UseTenantResolution()</c>: what a signed-in user of one tenant gets on another.
/// </summary>
public sealed class IdentityPipelineTests(SqlServerFixture fixture)
{
    private const string Password = "Pa55word!";

    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARefreshThatTakesClaimsFromARefusedTenant_IsNotWrittenToTheCookie()
    {
        // Users every tenant shares, signed in with the tenant they signed in to. The refresh keeps that claim and,
        // against the guide's warning, adds the current tenant's plan; Identity then renews the cookie.
        await using var app = await StartAsync<IdentityUser, SharedIdentityContext>((identity, tenant) => identity.OnRefreshingPrincipal = c =>
        {
            var newIdentity = (ClaimsIdentity)c.NewPrincipal!.Identity!;
            newIdentity.AddClaim(c.CurrentPrincipal!.FindFirst("tenant_id")!);

            if (tenant.CurrentTenantId == "globex")
            {
                newIdentity.AddClaim(new Claim("plan", "premium"));
            }

            return Task.CompletedTask;
        });
        using var client = app.GetTestClient();
        var cookie = await SignInAsync(app, client, new IdentityUser { UserName = "alice" });

        using (var allowed = await SendAsync(client, "acme", "/plan", cookie))
        {
            allowed.StatusCode.Should().Be(HttpStatusCode.Redirect, "Acme is not premium: access denied");
            AuthCookie(allowed).Should().NotBeNull("Identity renews the refreshed cookie on an allowed request");
        }

        using (var refused = await SendAsync(client, "globex", "/plan", cookie))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        using var me = await SendAsync(client, "acme", "/me", cookie);
        (await me.Content.ReadAsStringAsync(Ct)).Should().Be("alice");
    }

    [Fact]
    public async Task ATenantOwnedUser_IsSignedOutOnAnotherTenant_WhenTheSecurityStampIsChecked()
    {
        // The guide's set-up: each tenant's own users. On Globex, Acme's Alice is not found, so Identity's check rejects
        // the cookie and deletes it, before Tenantry sees a user: the request runs anonymous.
        await using var app = await StartAsync<TenantUser, IdentityContext>((_, _) => { });
        using var client = app.GetTestClient();
        var cookie = await SignInAsync(app, client, new TenantUser { UserName = "alice" });

        using var other = await SendAsync(client, "globex", "/me", cookie);
        (await other.Content.ReadAsStringAsync(Ct)).Should().Be("(anonymous)");
        AuthCookie(other).Should().Be(".AspNetCore.Identity.Application=", "the cookie is deleted");
    }

    [Fact]
    public async Task IdentitysCookiesNamedPerTenant_LeaveAUserOfOneTenantAnonymousOnAnother()
    {
        // The refresh keeps the tenant the user signed in to, as the guide's claims factory would add it.
        await using var app = await StartAsync<TenantUser, IdentityContext>(
            (identity, _) => identity.OnRefreshingPrincipal = c =>
            {
                ((ClaimsIdentity)c.NewPrincipal!.Identity!).AddClaim(c.CurrentPrincipal!.FindFirst("tenant_id")!);
                return Task.CompletedTask;
            },
            cookiePerTenant: true);
        using var client = app.GetTestClient();
        var cookie = await SignInAsync(app, client, new TenantUser { UserName = "alice" }, ".App.acme.Identity.Application=");

        // Globex's handlers read Globex's cookies only: Alice is anonymous there, and her Acme cookie is not deleted.
        using (var other = await SendAsync(client, "globex", "/me", cookie))
        {
            (await other.Content.ReadAsStringAsync(Ct)).Should().Be("(anonymous)");
            other.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        using var me = await SendAsync(client, "acme", "/me", cookie);
        (await me.Content.ReadAsStringAsync(Ct)).Should().Be("alice");
    }

    private async Task<WebApplication> StartAsync<TUser, TContext>(
        Action<SecurityStampValidatorOptions, ITenantContext<string>> stamp, bool cookiePerTenant = false)
        where TUser : IdentityUser
        where TContext : IdentityDbContext<TUser>
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTenantry<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme, Globex])
            .ValidateTenantAccess((http, t) =>
                http.User.Identity?.IsAuthenticated != true || http.User.FindFirst("tenant_id")?.Value == t.TenantId)
            .ConfigurePerTenant(perTenant =>
            {
                // The guide's names, for each of Identity's cookies.
                foreach (var scheme in (string[])[IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
                             IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme])
                {
                    perTenant.Configure<CookieAuthenticationOptions>(scheme, (o, t) =>
                    {
                        if (cookiePerTenant)
                            o.Cookie.Name = $".App.{t.TenantId}.{scheme}";
                    });
                }
            }));
        var connectionString = fixture.WithDatabase($"identity_{Guid.NewGuid():N}");
        builder.Services.AddDbContext<TContext>(options => options.UseSqlServer(connectionString).UseTenantry());
        builder.Services.AddIdentity<TUser, IdentityRole>().AddEntityFrameworkStores<TContext>();
        builder.Services.AddOptions<SecurityStampValidatorOptions>().Configure<ITenantContext<string>>((o, tenant) =>
        {
            o.ValidationInterval = TimeSpan.Zero;
            stamp(o, tenant);
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Premium", policy => policy.RequireClaim("plan", "premium"));

        var app = builder.Build();
        app.UseTenantResolution();
        app.UseAuthentication();
        app.UseTenantry();
        app.UseAuthorization();
        app.MapGet("/plan", () => "premium").RequireAuthorization("Premium");
        app.MapGet("/me", (HttpContext http) => http.User.Identity?.Name ?? "(anonymous)").AllowMissingTenant();
        app.MapPost("/sign-in", async (SignInManager<TUser> signIn, UserManager<TUser> users, ITenantContext<string> tenant) =>
        {
            var user = await users.FindByNameAsync("alice");
            await signIn.SignInWithClaimsAsync(user!, isPersistent: false, [new Claim("tenant_id", tenant.CurrentTenantId!)]);
        });

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreatedAsync(Ct);
        }

        await app.StartAsync(Ct);
        return app;
    }

    // Creates Alice as Acme's, and signs her in on Acme; the sign-in is anonymous, so the validator lets it have Acme.
    private static async Task<string> SignInAsync<TUser>(
        WebApplication app, HttpClient client, TUser alice, string cookie = ".AspNetCore.Identity.Application=")
        where TUser : IdentityUser
    {
        await using (var scope = app.Services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Acme))
        {
            (await scope.ServiceProvider.GetRequiredService<UserManager<TUser>>().CreateAsync(alice, Password)).Succeeded
                .Should().BeTrue();
        }

        using HttpRequestMessage request = new(HttpMethod.Post, "http://localhost/sign-in");
        request.Headers.Add("X-Tenant-Id", "acme");
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(cookie, StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string tenant, string path, string cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://localhost{path}");
        request.Headers.Add("X-Tenant-Id", tenant);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, Ct);
    }

    private static string? AuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal))
            : null;
}

/// <summary>Identity with users every tenant shares: not tenant-owned.</summary>
public sealed class SharedIdentityContext(DbContextOptions<SharedIdentityContext> options) : IdentityDbContext<IdentityUser>(options);
