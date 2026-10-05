using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tenantry.AspNetCore.Internal;

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
    public async Task EachTenant_CanAuthenticateWithASchemeOfItsOwn_ThroughAPolicyScheme()
    {
        // A scheme per identity provider, and a policy scheme as the default that forwards to the current tenant's.
        await using var app = await StartAsync(
            tenant => tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("example.com")).UseInMemoryStore([Acme, Globex]),
            services =>
            {
                var authentication = services.AddAuthentication("tenant")
                    .AddPolicyScheme("tenant", "The tenant's provider", o => o.ForwardDefaultSelector = http =>
                        http.RequestServices.GetRequiredService<ITenantContext<string>>().CurrentTenantId ?? "default");

                foreach (var provider in Keys.Keys)
                {
                    authentication.AddJwtBearer(provider, o => Validate(o, provider));
                }

                services.AddAuthorization();
            },
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/whoami", (HttpContext http, ITenantContext<string> tenant) => $"{tenant.CurrentTenantId}:{http.User.Identity!.Name}")
                    .RequireAuthorization();
            });

        (await Get(app, "acme", "/whoami", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme:alice"));
        (await Get(app, "globex", "/whoami", Token("globex", "bob"))).Should().Be((HttpStatusCode.OK, "globex:bob"));
        (await Get(app, "globex", "/whoami", Token("acme", "alice"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await Get(app, "acme", "/whoami", Token("default", "mallory"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await Get(app, null, "/whoami", Token("default", "carol"))).Should().Be((HttpStatusCode.OK, ":carol"));
    }

    [Fact]
    public async Task ATenantTheValidatorsRefuse_RefusesTheSignedInRequest_WhereverItGoes()
    {
        List<TenantRejectionReason> rejections = [];
        await using var app = await StartJwtAsync(tenant => tenant
            .ValidateTenantAccess((_, t) => t.TenantId != "globex")
            .ConfigureResolution(o => o.OnRejected = rejected =>
            {
                rejections.Add(rejected.Reason);
                return Task.CompletedTask;
            }));

        // Bob signed in as Globex, which he may not use: no endpoint runs with that user, one that needs no tenant too.
        (await Get(app, "globex", "/required", Token("globex", "bob"))).Status.Should().Be(HttpStatusCode.Forbidden);
        (await Get(app, "globex", "/tenant", Token("globex", "bob"))).Status.Should().Be(HttpStatusCode.Forbidden);
        rejections.Should().Equal(TenantRejectionReason.AccessDenied, TenantRejectionReason.AccessDenied);

        (await Get(app, "acme", "/tenant", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme"));
    }

    [Fact]
    public async Task AnAnonymousCallerTheValidatorsRefuse_CarriesNoClaims_SoAnEndpointThatNeedsNoTenantRunsWithNone()
    {
        await using var app = await StartJwtAsync(tenant => tenant.ValidateTenantAccessByClaim("tenant_id"));

        (await Get(app, "globex", "/tenant")).Should().Be((HttpStatusCode.OK, "(none)"));
        (await Get(app, "globex", "/required")).Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARequestThatNamesNoTenant_OrAnUnknownOne_HadNoTenantDuringAuthentication_AndRunsAsBefore()
    {
        await using var app = await StartJwtAsync(tenant => tenant.ValidateTenantAccessByClaim("tenant_id"));

        (await Get(app, null, "/tenant", Token("default", "carol", tenantClaim: "acme"))).Should().Be((HttpStatusCode.OK, "(none)"));
        (await Get(app, "initech", "/tenant", Token("default", "carol", tenantClaim: "acme"))).Should().Be((HttpStatusCode.OK, "(none)"));
        (await Get(app, "initech", "/required", Token("default", "carol", tenantClaim: "acme"))).Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AClaimThatNamesAnotherTenant_ThanTheOneAuthenticationRanUnder_RefusesTheRequest()
    {
        // The subdomain comes first, so authentication runs as Acme; the token says Globex, which the validator reads.
        await using var app = await StartJwtAsync(tenant => tenant.ResolveFromClaim().ValidateTenantAccessByClaim("tenant_id"));

        (await Get(app, "acme", "/tenant", Token("acme", "alice", tenantClaim: "acme"))).Should().Be((HttpStatusCode.OK, "acme"));
        (await Get(app, "acme", "/tenant", Token("acme", "alice", tenantClaim: "globex"))).Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnExceptionHandlerRunAgain_KeepsTheAllowedTenant_AndAStatusPage_DoesNotRunForARefusedOne()
    {
        await using var app = await StartAsync(
            tenant =>
            {
                JwtTenants(tenant);
                tenant.ValidateTenantAccessByClaim("tenant_id");
            },
            AddJwt,
            a =>
            {
                a.UseExceptionHandler("/error");
                a.UseStatusCodePagesWithReExecute("/status/{0}");
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/error", (ITenantContext<string> tenant) => $"error as {tenant.CurrentTenantId}").AllowMissingTenant();
                a.MapGet("/status/{code}", (string code, HttpContext http) => $"status page {code} for {http.User.Identity?.Name}")
                    .AllowMissingTenant();
                a.MapGet("/throws", string () => throw new InvalidOperationException("boom"));
            });

        (await Get(app, "acme", "/throws", Token("acme", "alice", tenantClaim: "acme")))
            .Should().Be((HttpStatusCode.InternalServerError, "error as acme"));

        // Refused again when the status page runs, so the page never runs with the user.
        var refused = await Get(app, "globex", "/tenant", Token("globex", "bob", tenantClaim: "acme"));
        refused.Status.Should().Be(HttpStatusCode.Forbidden);
        refused.Body.Should().NotContain("bob");
    }

    [Fact]
    public async Task ASuspendedTenant_IsNotCurrentForAuthentication_AndOnlyACallerTheValidatorsAllowLearnsItIsSuspended()
    {
        await using var app = await StartJwtAsync(tenant => tenant
            .ValidateTenantActivity(t => t.TenantId != "globex")
            .ValidateTenantAccess((http, _) => http.User.Identity?.Name == "bob")
            .ConfigureResolution(o => o.InactiveTenantStatusCode = StatusCodes.Status402PaymentRequired));

        // Globex's settings would accept its token; a suspended tenant's are never used, so no one is signed in.
        (await Get(app, "globex", "/required", Token("globex", "bob"))).Status.Should().Be(HttpStatusCode.Forbidden);
        (await Get(app, "globex", "/required", Token("default", "bob"))).Status.Should().Be(HttpStatusCode.PaymentRequired);
        (await Get(app, "globex", "/required", Token("default", "eve"))).Status.Should().Be(HttpStatusCode.Forbidden);
        (await Get(app, "globex", "/tenant", Token("default", "bob"))).Should().Be((HttpStatusCode.OK, "(none)"));
    }

    [Fact]
    public async Task WithNothingToResolveBeforeAuthentication_UseTenantryTriesTheClaimResolvers()
    {
        await using var app = await StartJwtAsync(tenant => tenant.ResolveFromClaim());

        // No subdomain: the default settings authenticate, then the claim names the tenant.
        (await Get(app, null, "/required", Token("default", "carol", tenantClaim: "acme"))).Should().Be((HttpStatusCode.OK, "acme"));
    }

    [Fact]
    public async Task AResolverAddedAfterAClaimResolver_NeverWinsOverTheClaim()
    {
        // Registration order: the token's claim first, a header second. Before authentication the claim cannot be read,
        // so the header must not be taken in its place.
        await using var app = await StartAsync(
            tenant => tenant
                .ResolveFromClaim()
                .ResolveFromHeader("X-Tenant-Id")
                .UseInMemoryStore([Acme, Globex]),
            AddJwt,
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
            });
        using var client = app.GetTestClient();

        async Task<string> TenantOf(HttpClient http, string? claim, string? header)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "http://localhost/tenant");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("default", "dave", tenantClaim: claim));

            if (header is not null)
            {
                request.Headers.Add("X-Tenant-Id", header);
            }

            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        (await TenantOf(client, claim: "acme", header: "globex")).Should().Be("acme", "the claim was added first");
        (await TenantOf(client, claim: null, header: "globex")).Should().Be("globex", "with no claim, the next resolver applies");
    }

    [Fact]
    public async Task ThePropagationHeader_IsReadOnlyFromATrustedCaller_AfterAuthentication()
    {
        // The header first, then a header any user can send. The caller is trusted by its token, which is not read
        // until authentication has run, so the early pass must not take the second header in the first one's place.
        await using var app = await StartAsync(
            tenant => tenant
                .ResolveFromPropagationHeader(http => http.User.FindFirst("sub")?.Value == "orders-service")
                .ResolveFromHeader("X-Tenant-Id")
                .UseInMemoryStore([Acme, Globex]),
            AddJwt,
            pipeline: a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
            });
        using var client = app.GetTestClient();

        async Task<string> TenantOf(HttpClient http, string caller, string? propagated, string? header)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "http://localhost/tenant");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("default", caller));

            if (propagated is not null)
                request.Headers.Add(TenantPropagation.HeaderName, propagated);

            if (header is not null)
                request.Headers.Add("X-Tenant-Id", header);

            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        (await TenantOf(client, "orders-service", propagated: "globex", header: "acme")).Should().Be("globex", "a trusted caller's header comes first");
        (await TenantOf(client, "mallory", propagated: "globex", header: "acme")).Should().Be("acme", "an untrusted caller's header is ignored");
    }

    [Fact]
    public async Task AResolverAddedBeforeAClaimResolver_ResolvesBeforeAuthentication()
    {
        // The subdomain comes first, so it resolves before authentication and the tenant's settings authenticate.
        await using var app = await StartJwtAsync(tenant => tenant.ResolveFromClaim());

        (await Get(app, "acme", "/whoami", Token("acme", "alice", tenantClaim: "globex"))).Should().Be((HttpStatusCode.OK, "acme:alice"));
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
    public async Task APolicyThatReadsTheTenant_SeesOnlyATenantTheValidatorsAllow_WithAuthorizationAfterUseTenantry()
    {
        await using var app = await StartPremiumAsync(a =>
        {
            a.UseTenantResolution();
            a.UseAuthentication();
            a.UseTenantry();
            a.UseAuthorization();
        });

        // Globex is premium, but this caller's token names Acme: the validator refuses Globex, so the policy sees none.
        (await GetPremium(app, "globex", claim: "acme")).Should().Be(HttpStatusCode.Forbidden);
        (await GetPremium(app, "globex", claim: "globex")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UseAuthorization_BetweenUseTenantResolutionAndUseTenantry_FailsTheApplicationsStart()
    {
        // In this order the policy would see Globex before the validator refused it, and let the Acme caller in.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry<string>(PremiumTenants);
        AddPremiumPolicy(builder.Services);
        await using var app = builder.Build();
        app.UseTenantResolution();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Invoking(a => a.UseTenantry())
            .Should().Throw<InvalidOperationException>().WithMessage("*UseAuthorization()*after app.UseTenantry()*");
    }

    [Fact]
    public async Task AuthorizationTheStartupCheckCannotSee_BeforeUseTenantry_RefusesTheRequest()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartPremiumAsync(
            a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                // Authorization added another way (a library's middleware, say), which the startup check does not see.
                a.UseMiddleware<AuthorizationMiddleware>();
                a.UseTenantry();
                a.UseAuthorization();
            },
            logs);

        (await GetPremium(app, "globex", claim: "acme")).Should().Be(HttpStatusCode.InternalServerError);
        (await GetPremium(app, "globex", claim: "globex")).Should().Be(HttpStatusCode.InternalServerError);
        logs.For(1013).Should().HaveCount(2).And.AllSatisfy(e => e.Message.Should().Contain("/premium"));
    }

    [Fact]
    public async Task AspNetCoresAuthorizationMarkers_AreTheOnesTheOrderChecksRead()
    {
        // Both keys are ASP.NET Core's own, undocumented: WebApplication reads the first, the endpoint middleware the
        // second. If either changes, the order checks above stop working, and this test says why.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapGet("/marker", (HttpContext http) => http.Items.ContainsKey(AuthorizationMarkers.MiddlewareRan));
        await app.StartAsync(TestContext.Current.CancellationToken);

        ((IApplicationBuilder)app).Properties.Should().ContainKey(AuthorizationMarkers.MiddlewareAdded);
        using var client = app.GetTestClient();
        (await client.GetStringAsync("/marker", TestContext.Current.CancellationToken)).Should().Be("true");
    }

    [Fact]
    public async Task TheStartupProbe_FindsTheMarkersOfTheRunningAspNetCore_AndNamesAKeyItDoesNotSet()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();

        AuthorizationMarkers.Missing(app.Services).Should().BeEmpty();
        AuthorizationMarkers.Missing(app.Services, added: "__Renamed").Should().Equal("__Renamed");
        AuthorizationMarkers.Missing(app.Services, ran: "__Renamed").Should().Equal("__Renamed");
    }

    [Fact]
    public async Task UseTenantResolution_LogsNoMissingMarkers_OnThisAspNetCore()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartPremiumAsync(
            a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
            },
            logs);

        (await GetPremium(app, "globex", claim: "globex")).Should().Be(HttpStatusCode.OK);
        logs.For(1014).Should().BeEmpty();
    }

    // Authentication code that reads the current tenant (an event, a claims transformation) adds a claim from the
    // tenant the request names. With app.UseTenantResolution() that tenant is current during authentication, before
    // the validator refuses it, so the user would carry Globex's claim: the request is refused. Without it,
    // authentication ran with no tenant, the user carries nothing of Globex's, and the policy refuses it.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task AClaimDerivedFromTheTenantDuringAuthentication_NeverReachesAuthorization_WhenTheTenantIsRefused(
        bool earlyResolution, bool fromTransformation)
    {
        static void AddPlanClaim(ClaimsPrincipal user, IServiceProvider services)
        {
            // "The tenant's plan", read from the current tenant: Globex is premium.
            if (services.GetRequiredService<ITenantContext<string>>().CurrentTenantId == "globex" &&
                user.Identity is ClaimsIdentity identity && !user.HasClaim("plan", "premium"))
            {
                identity.AddClaim(new Claim("plan", "premium"));
            }
        }

        await using var app = await StartAsync(
            PremiumTenants,
            services =>
            {
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
                {
                    Validate(o, "default");

                    if (!fromTransformation)
                    {
                        o.Events = new JwtBearerEvents
                        {
                            OnTokenValidated = c =>
                            {
                                AddPlanClaim(c.Principal!, c.HttpContext.RequestServices);
                                return Task.CompletedTask;
                            },
                        };
                    }
                });

                if (fromTransformation)
                {
                    services.AddTransient<IClaimsTransformation>(sp => new PlanTransformation(p =>
                    {
                        AddPlanClaim(p, sp);
                        return p;
                    }));
                }

                services.AddAuthorizationBuilder().AddPolicy("Premium", policy => policy.RequireClaim("plan", "premium"));
            },
            a =>
            {
                if (earlyResolution)
                {
                    a.UseTenantResolution();
                }

                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/plan", () => "premium").RequireAuthorization("Premium");
            });

        // Alice's token lists only Acme.
        (await GetPremium(app, "globex", claim: "acme", path: "/plan")).Should().Be(HttpStatusCode.Forbidden);

        // Without the early step, an endpoint that needs no tenant still runs, with none.
        if (!earlyResolution)
        {
            using var client = app.GetTestClient();
            using HttpRequestMessage request = new(HttpMethod.Get, "http://localhost/tenant");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("default", "alice", tenantClaim: "acme"));
            request.Headers.Add("X-Tenant-Id", "globex");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("(none)");
        }
    }

    private sealed class PlanTransformation(Func<ClaimsPrincipal, ClaimsPrincipal> transform) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(transform(principal));
    }

    [Fact]
    public async Task ARefusedRequest_CarriesNoRenewedCookie_SoTheTenantsClaimsNeverLeaveIt()
    {
        await using var app = await StartPlanCookieAsync();
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);
        var acmeCookie = AuthCookie(signIn, "auth")!;

        using (var plan = await SendAs(client, "acme", "/plan", acmeCookie))
        {
            plan.StatusCode.Should().Be(HttpStatusCode.Redirect, "Acme is not premium");
            AuthCookie(plan, "auth").Should().NotBeNull("an allowed request still renews its cookie");
        }

        // Refused, and signed out, but the deletion is dropped with the renewal: the browser keeps its own cookie,
        // whose ticket was never changed, and stays signed in to Acme.
        using (var refused = await SendAs(client, "globex", "/plan", acmeCookie))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        (await ReadAsync(await SendAs(client, "acme", "/me", acmeCookie))).Should().Be("acme");
    }

    [Fact]
    public async Task ARefusedRequest_WithASessionStore_EndsTheSession_RatherThanStoreTheTenantsClaims()
    {
        RecordingTicketStore store = new();
        await using var app = await StartPlanCookieAsync(o => o.SessionStore = store);
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);
        var acmeCookie = AuthCookie(signIn, "auth")!;

        using (var refused = await SendAs(client, "globex", "/plan", acmeCookie))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        store.Renewed.Should().NotContain(ticket => ticket.Principal.HasClaim("plan", "premium"));
        store.Removed.Should().ContainSingle();

        // The session is gone, so the cookie the browser kept names none: Alice is signed out of Acme too.
        using (var plan = await SendAs(client, "acme", "/plan", acmeCookie))
        {
            plan.StatusCode.Should().NotBe(HttpStatusCode.OK);
        }

        (await ReadAsync(await SendAs(client, "acme", "/me", acmeCookie))).Should().Be("(anonymous)");
    }

    [Fact]
    public async Task ARefusedRequest_SignsOutTheCookieUnderARemoteDefaultScheme_SoItsStoreKeepsNothingOfTheTenant()
    {
        // The default authenticate scheme is a remote one (OpenID Connect, as Microsoft.Identity.Web sets it up), which
        // authenticates through the cookie and returns the ticket under its own name.
        RecordingTicketStore store = new();
        await using var app = await StartPlanCookieAsync(
            o => o.SessionStore = store,
            more: authentication =>
            {
                authentication.AddScheme<AuthenticationSchemeOptions, RemoteStyleHandler>("remote", null);
                authentication.Services.Configure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = "remote");
            });
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);
        var acmeCookie = AuthCookie(signIn, "auth")!;

        using (var refused = await SendAs(client, "globex", "/plan", acmeCookie))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        store.Renewed.Should().NotContain(ticket => ticket.Principal.HasClaim("plan", "premium"));
        store.Removed.Should().ContainSingle();

        using var plan = await SendAs(client, "acme", "/plan", acmeCookie);
        plan.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ARefusedRequestToTheLogoutPath_CarriesNoRedirect()
    {
        await using var app = await StartPlanCookieAsync(o => o.LogoutPath = "/logout");
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);

        // The sign-out on the logout path redirects to the ReturnUrl; the refusal keeps none of it.
        using var refused = await SendAs(client, "globex", "/logout?ReturnUrl=%2Fsomewhere", AuthCookie(signIn, "auth"));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refused.Headers.Location.Should().BeNull();
        refused.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task ASignOutThatFails_IsLogged_AndTheOtherSchemesAndTheRejectionStillHappen()
    {
        RecordingLoggerProvider logs = new();
        var otherSignedOut = false;
        await using var app = await StartPlanCookieAsync(
            o => o.SessionStore = new RecordingTicketStore { FailRemoving = true },
            more: authentication => authentication.AddCookie("other", o => o.Events.OnSigningOut = _ =>
            {
                otherSignedOut = true;
                return Task.CompletedTask;
            }),
            logs: logs);
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);

        using (var refused = await SendAs(client, "globex", "/plan", AuthCookie(signIn, "auth")))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        logs.For(1015).Should().ContainSingle().Which.Message.Should().Contain(CookieAuthenticationDefaults.AuthenticationScheme);
        otherSignedOut.Should().BeTrue();
    }

    // Like OpenID Connect as the default authenticate scheme: handles requests of its own, and authenticates through
    // the cookie, returning the ticket under its own name.
    private sealed class RemoteStyleHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationRequestHandler
    {
        public Task<bool> HandleRequestAsync() => Task.FromResult(false);

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var cookie = await Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return cookie.Succeeded
                ? AuthenticateResult.Success(new AuthenticationTicket(cookie.Principal!, cookie.Properties, Scheme.Name))
                : cookie;
        }
    }

    [Fact]
    public async Task ACookieNamePerTenant_LeavesAnotherTenantsCookieUnread()
    {
        await using var app = await StartPlanCookieAsync(tenants: tenant => tenant.ConfigurePerTenant(perTenant => perTenant
            .Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, (o, t) =>
                o.Cookie.Name = $".App.{t.TenantId}")));
        using var client = app.GetTestClient();

        using var signIn = await SendAs(client, "acme", "/sign-in", cookie: null);
        var acmeCookie = AuthCookie(signIn, ".App.acme")!;

        // On Globex the browser's Acme cookie is not Globex's: the caller is anonymous, and nothing is refused,
        // renewed or deleted.
        using (var plan = await SendAs(client, "globex", "/plan", acmeCookie))
        {
            plan.StatusCode.Should().Be(HttpStatusCode.Redirect, "an anonymous caller is sent to sign in");
            plan.Headers.Contains("Set-Cookie").Should().BeFalse();
        }

        using (var me = await SendAs(client, "globex", "/me", acmeCookie))
        {
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadAsync(me)).Should().Be("(anonymous)");
        }

        using (var globexSignIn = await SendAs(client, "globex", "/sign-in", acmeCookie))
        {
            AuthCookie(globexSignIn, ".App.globex").Should().NotBeNull("Globex's sign-in page works for Acme's user");
        }

        (await ReadAsync(await SendAs(client, "acme", "/me", acmeCookie))).Should().Be("acme");
    }

    // Cookie authentication whose event takes the plan from the current tenant and renews the cookie, as Identity's
    // security stamp check renews it: the renewal is written as the response starts, after Tenantry refused the request.
    private static Task<WebApplication> StartPlanCookieAsync(
        Action<CookieAuthenticationOptions>? cookie = null,
        Action<ITenantBuilder<string>>? tenants = null,
        Action<AuthenticationBuilder>? more = null,
        RecordingLoggerProvider? logs = null) =>
        StartAsync(
            tenant =>
            {
                PremiumTenants(tenant);
                tenants?.Invoke(tenant);
            },
            services =>
            {
                var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
                {
                    o.Cookie.Name = "auth";
                    o.Events.OnValidatePrincipal = c =>
                    {
                        var tenant = c.HttpContext.RequestServices.GetRequiredService<ITenantContext<string>>().CurrentTenantId;

                        if (tenant == "globex")
                        {
                            ClaimsIdentity premium = new(c.Principal!.Claims, c.Principal.Identity!.AuthenticationType);
                            premium.AddClaim(new Claim("plan", "premium"));
                            c.ReplacePrincipal(new ClaimsPrincipal(premium));
                        }

                        // Renewed on every request, so an allowed request shows renewal still works.
                        c.ShouldRenew = tenant is not null;
                        return Task.CompletedTask;
                    };
                    cookie?.Invoke(o);
                });
                more?.Invoke(authentication);
                services.AddAuthorizationBuilder().AddPolicy("Premium", policy => policy.RequireClaim("plan", "premium"));
            },
            a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/sign-in", async http =>
                {
                    ClaimsIdentity identity = new(
                        [new Claim("sub", "alice"), new Claim("tenant_id", http.Request.Headers["X-Tenant-Id"].ToString())],
                        CookieAuthenticationDefaults.AuthenticationScheme);
                    await http.SignInAsync(new ClaimsPrincipal(identity));
                }).AllowMissingTenant();
                a.MapGet("/plan", () => "premium").RequireAuthorization("Premium");
                a.MapGet("/me", (HttpContext http) => http.User.FindFirst("tenant_id")?.Value ?? "(anonymous)").AllowMissingTenant();
            },
            logs);

    private static async Task<HttpResponseMessage> SendAs(HttpClient client, string tenant, string path, string? cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://localhost{path}");
        request.Headers.Add("X-Tenant-Id", tenant);

        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        using (response)
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static string? AuthCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith($"{name}=", StringComparison.Ordinal))
            : null;

    // Keeps tickets in memory, and records what the cookie handler renews and removes.
    private sealed class RecordingTicketStore : ITicketStore
    {
        public bool FailRemoving { get; init; }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthenticationTicket> _tickets = new();

        public System.Collections.Concurrent.ConcurrentQueue<AuthenticationTicket> Renewed { get; } = new();

        public System.Collections.Concurrent.ConcurrentQueue<string> Removed { get; } = new();

        public Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            var key = Guid.NewGuid().ToString("N");
            _tickets[key] = ticket;
            return Task.FromResult(key);
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket)
        {
            Renewed.Enqueue(ticket);
            _tickets[key] = ticket;
            return Task.CompletedTask;
        }

        public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
            Task.FromResult(_tickets.GetValueOrDefault(key));

        public Task RemoveAsync(string key)
        {
            if (FailRemoving)
                throw new InvalidOperationException("The session store is down.");

            Removed.Enqueue(key);
            _tickets.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ASignedInIdentityWithNoClaims_IsRefusedToo()
    {
        await using var app = await StartAsync(
            PremiumTenants,
            services =>
            {
                services.AddAuthentication("guest").AddScheme<AuthenticationSchemeOptions, GuestUnderGlobex>("guest", null);
                services.AddAuthorization();
            },
            a =>
            {
                a.UseTenantResolution();
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/signed-in", () => "signed in").RequireAuthorization();
            });

        (await GetPremium(app, "globex", claim: "acme", path: "/signed-in")).Should().Be(HttpStatusCode.Forbidden);
    }

    // Signs in, with an identity that has no claims, a request made while Globex is current.
    private sealed class GuestUnderGlobex(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Context.RequestServices.GetRequiredService<ITenantContext<string>>().CurrentTenantId == "globex"
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Guest")), Scheme.Name))
                : AuthenticateResult.NoResult());
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
    public async Task RouteValueResolution_WithRoutingAfterUseTenantResolution_AuthenticatesWithoutTheTenant_AndIsLoggedOnce()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartRoutedAsync(RouteJwtTenants, routingFirst: false, logs);

        // An endpoint without the route value the resolver reads is not warned about.
        (await Get(app, null, "/tenant")).Body.Should().Be("(none)");
        logs.For(1016).Should().BeEmpty();

        // The route names Acme, but authentication ran with no tenant's settings, which refuse Acme's token.
        (await Get(app, null, "/acme/whoami", Token("acme", "alice"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await Get(app, null, "/acme/whoami", Token("acme", "alice"))).Status.Should().Be(HttpStatusCode.Unauthorized);

        logs.For(1016).Should().ContainSingle().Which.Message.Should().Contain("/{tenant}/whoami")
            .And.Contain("app.UseRouting() before app.UseTenantResolution()");
    }

    [Fact]
    public async Task RouteValueResolution_WithRoutingFirst_AuthenticatesWithTheTenant_AndLogsNoOrderingWarning()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartRoutedAsync(RouteJwtTenants, routingFirst: true, logs);

        (await Get(app, null, "/acme/whoami", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme:alice"));

        logs.For(1016).Should().BeEmpty();
    }

    [Fact]
    public async Task RoutingAfterUseTenantResolution_WithoutARouteValueResolver_LogsNoOrderingWarning()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartRoutedAsync(JwtTenants, routingFirst: false, logs);

        (await Get(app, "acme", "/acme/whoami", Token("acme", "alice"))).Should().Be((HttpStatusCode.OK, "acme:alice"));

        logs.For(1016).Should().BeEmpty();
    }

    [Fact]
    public async Task WebApplicationsOwnUseAuthentication_RunsTooEarly_AndIsLogged()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartAsync(
            JwtTenants,
            AddJwt,
            pipeline: a =>
            {
                // No app.UseAuthentication(): WebApplication adds one before this middleware.
                a.UseTenantResolution();
                a.UseTenantry();
            },
            logs);

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
                .ConfigurePerTenant(perTenant => perTenant.Configure<CookieAuthenticationOptions>(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    (o, t) => o.Cookie.Name = $"auth-{t.Name.ToLowerInvariant()}")),
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
                a.MapGet("/signed-in-to", (HttpContext http) => http.User.FindFirst("tenant_id")?.Value ?? "(anonymous)")
                    .AllowMissingTenant();
            });
        using var client = app.GetTestClient();

        // Acme's sign-in sets Acme's cookie, which Acme accepts.
        var signIn = await Send(client, "acme", "/sign-in");
        var cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("auth-acme=", StringComparison.Ordinal));
        var value = cookie.Split(';')[0]["auth-acme=".Length..];

        (await Send(client, "acme", "/required", cookie: $"auth-acme={value}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The same ticket under Globex's cookie name decrypts there (one key ring), but names Acme, so the validator
        // refuses Globex, and the request with it: an endpoint that needs no tenant does not run with that user either.
        (await Send(client, "globex", "/required", cookie: $"auth-globex={value}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(client, "globex", "/signed-in-to", cookie: $"auth-globex={value}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static void JwtTenants(ITenantBuilder<string> tenant) =>
        tenant
            .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
            .UseInMemoryStore([Acme, Globex])
            .ConfigurePerTenant(perTenant => perTenant.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, TenantIssuer));

    // The tenant comes from the route: /{tenant}/whoami.
    private static void RouteJwtTenants(ITenantBuilder<string> tenant) =>
        tenant
            .ResolveFromRouteValue()
            .UseInMemoryStore([Acme, Globex])
            .ConfigurePerTenant(perTenant => perTenant.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, TenantIssuer));

    // Accepts only the tokens of the tenant's own issuer and key.
    private static void TenantIssuer(JwtBearerOptions o, ITenantDescriptor<string> t)
    {
        o.TokenValidationParameters.ValidIssuer = Issuer(t.Name.ToLowerInvariant());
        o.TokenValidationParameters.IssuerSigningKey = Keys[t.Name.ToLowerInvariant()];
    }

    // UseTenantResolution(), authentication, UseTenantry() and authorization, with routing where the test puts it.
    private static Task<WebApplication> StartRoutedAsync(
        Action<ITenantBuilder<string>> configure,
        bool routingFirst,
        RecordingLoggerProvider logs) =>
        StartAsync(
            configure,
            AddJwt,
            pipeline: a =>
            {
                if (routingFirst)
                {
                    a.UseRouting();
                }

                a.UseTenantResolution();

                if (!routingFirst)
                {
                    a.UseRouting();
                }

                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/{tenant}/whoami", (HttpContext http, ITenantContext<string> current) => $"{current.CurrentTenantId}:{http.User.Identity!.Name}")
                    .RequireAuthorization();
            },
            logs);

    private static void AddJwt(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => Validate(o, "default"));
        services.AddAuthorization();
    }

    // Accepts the tokens of one issuer and key.
    private static void Validate(JwtBearerOptions o, string issuer)
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer(issuer),
            ValidAudience = "api",
            IssuerSigningKey = Keys[issuer],
            NameClaimType = "sub",
        };
    }

    // A header names the tenant, the token's claim must list it, and a policy that reads the current tenant lets in only
    // Globex's requests: /premium requires the policy, and no tenant.
    private static void PremiumTenants(ITenantBuilder<string> tenant) =>
        tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme, Globex]).ValidateTenantAccessByClaim("tenant_id");

    private static void AddPremiumPolicy(IServiceCollection services)
    {
        AddJwt(services);
        services.AddAuthorizationBuilder().AddPolicy("PremiumTenant", policy => policy.RequireAssertion(context =>
            context.Resource is HttpContext http &&
            http.RequestServices.GetRequiredService<ITenantContext<string>>().CurrentTenantId == "globex"));
    }

    private static Task<WebApplication> StartPremiumAsync(Action<WebApplication> pipeline, RecordingLoggerProvider? logs = null) =>
        StartAsync(
            PremiumTenants,
            AddPremiumPolicy,
            a =>
            {
                pipeline(a);
                a.MapGet("/premium", () => "premium").RequireAuthorization("PremiumTenant");
            },
            logs);

    private static async Task<HttpStatusCode> GetPremium(WebApplication app, string tenant, string claim, string path = "/premium")
    {
        using var client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://localhost{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("default", "alice", tenantClaim: claim));
        request.Headers.Add("X-Tenant-Id", tenant);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
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

        app.MapGet("/tenant", Current);
        app.MapGet("/required", Current).RequireTenant();
        app.MapGet("/skipped", Current);

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
