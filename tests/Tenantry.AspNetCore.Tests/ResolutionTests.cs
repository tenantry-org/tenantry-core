using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Resolution by identifier, resolvers and validators from the request's scope, the resolution events, the startup
/// check of the pipeline, the one-time ordering warnings, and the logs and metrics of <c>app.UseTenantry()</c>.
/// </summary>
public sealed class ResolutionTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme Corp" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex LLC" };

    [Fact]
    public async Task AGuidKeyedStore_ResolvesSubdomainsAndCustomDomains_ByItsOwnMapping()
    {
        SlugStore.Lookups.Clear();
        await using var app = await StartAsync<Guid>(tenant => tenant
            .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
            .ResolveFromHost(o => o.ExcludedDomains.Add("example.com"))
            .UseStore<SlugStore>());
        using var client = app.GetTestClient();

        (await client.GetStringAsync("http://acme.example.com/tenant", TestContext.Current.CancellationToken)).Should().Be(SlugStore.Acme.ToString());
        (await client.GetStringAsync("http://APP.acme.com/tenant", TestContext.Current.CancellationToken)).Should().Be(SlugStore.Acme.ToString());
        (await client.GetStringAsync("http://www.example.com/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");
        (await client.GetStringAsync("http://example.com/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");
        (await client.GetStringAsync($"http://{SlugStore.Acme}.example.com/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)", "this store maps slugs, not ids");

        // The application's own hosts never reach the store.
        SlugStore.Lookups.Should().Equal("acme", "app.acme.com", SlugStore.Acme.ToString());
    }

    [Fact]
    public async Task CacheTenants_ServesTheMiddlewaresLookups_UntilTheTenantIsInvalidated()
    {
        CountingStore store = new();
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseStore(_ => store)
            .CacheTenants());
        using var client = app.GetTestClient();

        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);
        store.Lookups.Should().Be(1);

        await app.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);
        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);
        store.Lookups.Should().Be(2);
    }

    [Fact]
    public async Task ResolversAndValidatorsAddedByType_AreCreatedInTheRequestsOwnScope()
    {
        await using var app = await StartAsync<string>(
            tenant =>
            {
                tenant.UseInMemoryStore([Acme]);
                tenant.ValidateTenantAccess<ScopedValidator>();
                tenant.UseResolver<ScopedResolver>();
            },
            services => services.AddScoped<RequestServices>());
        using var client = app.GetTestClient();

        // The endpoint's own scoped service is the one the resolver and the validator were given.
        var first = await client.GetStringAsync("/scoped", TestContext.Current.CancellationToken);
        var second = await client.GetStringAsync("/scoped", TestContext.Current.CancellationToken);

        first.Should().MatchRegex("^acme [0-9a-f-]{36} same same$");
        second.Should().MatchRegex("^acme [0-9a-f-]{36} same same$");
        second.Split(' ')[1].Should().NotBe(first.Split(' ')[1], "each request has its own scope");
    }

    [Fact]
    public async Task AValidatorAddedByType_IsCreatedInEachRequestsScope_AndRunsInOrderWithTheOthers()
    {
        List<string> calls = [];
        await using var app = await StartAsync<string>(
            tenant =>
            {
                tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme, Globex]).RequireTenantByDefault()
                    .ValidateTenantAccess((_, _) => Record(calls, "first", allow: true));
                tenant.ValidateTenantAccess<MembershipValidator>();
                tenant.ValidateTenantAccess((_, _) => Record(calls, "last", allow: true));
            },
            services => services.AddScoped(_ => new Membership(calls, "acme")));
        using var client = app.GetTestClient();

        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);
        calls.Should().Equal("first", "membership", "last");

        calls.Clear();
        (await Get(client, "globex")).Should().Be(HttpStatusCode.Forbidden);
        calls.Should().Equal("first", "membership");
    }

    [Fact]
    public async Task OnResolved_RunsWithTheTenantCurrent()
    {
        List<string> seen = [];
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme])
            .ConfigureResolution(o => o.OnResolved = context =>
            {
                var current = context.HttpContext.RequestServices.GetRequiredService<ITenantContext<string>>();
                seen.Add($"{context.Tenant.TenantId}:{current.CurrentTenantId}");
                return Task.CompletedTask;
            }));
        using var client = app.GetTestClient();

        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "unknown")).Should().Be(HttpStatusCode.OK);

        seen.Should().Equal("acme:acme");
    }

    [Fact]
    public async Task OnRejected_IsToldTheReason_TheIdentifierAndTheTenant()
    {
        List<string> seen = [];
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme, Globex])
            .RequireTenantByDefault()
            .ValidateTenantAccess((_, t) => t.TenantId != "globex")
            .ConfigureResolution(o => o.OnRejected = context =>
            {
                seen.Add($"{context.Reason} {context.StatusCode} {context.Identifier ?? "-"} {context.Tenant?.TenantId ?? "-"}");
                return Task.CompletedTask;
            }));
        using var client = app.GetTestClient();

        (await Get(client, null)).Should().Be(HttpStatusCode.BadRequest);
        (await Get(client, "initech")).Should().Be(HttpStatusCode.Forbidden);
        (await Get(client, "globex")).Should().Be(HttpStatusCode.Forbidden);
        (await Get(client, "acme")).Should().Be(HttpStatusCode.OK);

        // With access validators, an unknown tenant gets the access-denied status, though the handler knows why.
        seen.Should().Equal("Missing 400 - -", "NotFound 403 initech -", "AccessDenied 403 globex globex");
    }

    [Fact]
    public async Task OnRejected_CanWriteItsOwnResponse_OrChangeTheStatus()
    {
        await using var app = await StartAsync<string>(
            tenant => tenant
                .ResolveFromHeader("X-Tenant-Id")
                .UseInMemoryStore([Acme])
                .RequireTenantByDefault()
                .ConfigureResolution(o => o.OnRejected = context =>
                {
                    if (context.Reason == TenantRejectionReason.Missing)
                    {
                        context.HttpContext.Response.Redirect("/welcome");
                        context.HandleResponse();
                    }
                    else
                    {
                        context.StatusCode = StatusCodes.Status410Gone;
                    }

                    return Task.CompletedTask;
                }),
            services => services.AddProblemDetails());
        using var client = app.GetTestClient();

        var missing = await client.GetAsync("/tenant", TestContext.Current.CancellationToken);
        missing.StatusCode.Should().Be(HttpStatusCode.Redirect);
        missing.Headers.Location!.ToString().Should().Be("/welcome");
        (await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().BeEmpty("the handler wrote the response");

        client.DefaultRequestHeaders.Add("X-Tenant-Id", "initech");
        var unknown = await client.GetAsync("/tenant", TestContext.Current.CancellationToken);
        unknown.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("\"status\":410").And.Contain("Tenant not found");
    }

    [Fact]
    public async Task WithoutUseTenantry_TheApplicationDoesNotStart()
    {
        var act = () => StartAsync<string>(
            tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]),
            pipeline: _ => { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*app.UseTenantry() is not in the request pipeline*endpoints that require one would run without it*");
    }

    [Fact]
    public async Task UseTenantryInABranch_Starts()
    {
        await using var app = await StartAsync<string>(
            tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]),
            pipeline: a => a.Map("/api", api => api.UseTenantry()));

        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task AHostThatServesNoRequests_IsNotChecked()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTenantry<string>(tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]));
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.Should().BeTrue();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UseTenantryBeforeRouting_IsLoggedOnce()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]),
            pipeline: a =>
            {
                a.UseTenantry();
                a.UseRouting();
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        // The middleware ran before routing chose the endpoint, which requires a tenant: the request is still rejected.
        (await client.GetAsync("/required", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/required", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        logs.For(1007).Should().ContainSingle().Which.Message.Should().Contain("/required").And.Contain("app.UseRouting()");
    }

    [Fact]
    public async Task UseTenantryBeforeRouting_ServesTenantsAndEndpointsThatNeedNone()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]),
            pipeline: a =>
            {
                a.UseTenantry();
                a.UseRouting();
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        (await Get(client, "acme", "/required")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "globex", "/required")).Should().Be(HttpStatusCode.NotFound);
        (await client.GetStringAsync("/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");
        (await client.GetAsync("/nowhere", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        logs.For(1003).Should().BeEmpty();
    }

    [Fact]
    public async Task UseTenantryInTheRightPlace_LogsNoOrderingWarning()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromClaim().UseInMemoryStore([Acme]),
            services => services.AddAuthentication(TestAuthentication.Name)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Name, null),
            pipeline: a =>
            {
                a.UseAuthentication();
                a.UseTenantry();
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/required", TestContext.Current.CancellationToken)).Should().Be("acme");
        (await client.GetAsync("/nowhere", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        logs.For(1007).Should().BeEmpty();
        logs.For(1008).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimResolutionBeforeAuthentication_IsLoggedOnce()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromClaim().UseInMemoryStore([Acme]),
            services => services.AddAuthentication(TestAuthentication.Name)
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Name, null),
            pipeline: a =>
            {
                a.UseTenantry();
                a.UseAuthentication();
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");
        (await client.GetStringAsync("/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");

        logs.For(1008).Should().ContainSingle().Which.Message.Should().Contain("app.UseAuthentication() before app.UseTenantry()");
    }

    [Fact]
    public async Task AUserSignedInLater_ByAuthorizationWithAnotherScheme_OrTheEndpoint_IsNotAnOrderingMistake()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromClaim().UseInMemoryStore([Acme]),
            services =>
            {
                services.AddAuthentication(NoUser.Name)
                    .AddScheme<AuthenticationSchemeOptions, NoUser>(NoUser.Name, null)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Name, null);
                services.AddAuthorization();
            },
            pipeline: a =>
            {
                a.UseAuthentication();
                a.UseTenantry();
                a.UseAuthorization();
                a.MapGet("/other-scheme", (ITenantContext<string> t) => t.CurrentTenantId ?? "(none)")
                    .RequireAuthorization(p => p.AddAuthenticationSchemes(TestAuthentication.Name).RequireAuthenticatedUser());
                a.MapGet("/sign-in", (HttpContext http) =>
                {
                    http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "acme")], "Login"));
                    return "signed in";
                });
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/other-scheme", TestContext.Current.CancellationToken)).Should().Be("(none)");
        (await client.GetStringAsync("/sign-in", TestContext.Current.CancellationToken)).Should().Be("signed in");

        logs.For(1008).Should().BeEmpty();
    }

    [Fact]
    public async Task AUserSignedIn_WithNoAuthenticationMiddleware_IsNotAnOrderingMistake()
    {
        var (app, logs) = await StartWithLogsAsync<string>(
            tenant => tenant.ResolveFromClaim().UseInMemoryStore([Acme]),
            pipeline: a =>
            {
                a.UseTenantry();
                a.MapGet("/sign-in", (HttpContext http) =>
                {
                    http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "acme")], "Login"));
                    return "signed in";
                });
            });
        await using var _ = app;
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/sign-in", TestContext.Current.CancellationToken)).Should().Be("signed in");

        logs.For(1008).Should().BeEmpty("no authentication middleware ran, so none ran too late");
    }

    [Fact]
    public async Task WithoutIServiceProviderIsService_AnUnknownTenantGetsTheAccessDeniedResponse()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore([Acme]));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        TenantResolutionMiddleware<string> middleware = new(
            _ => Task.CompletedTask,
            new TenantRequestResolution<string>(provider.GetRequiredService<ITenantLookup<string>>(), new NoServices()),
            provider.GetRequiredService<ITenantContextSetter<string>>(),
            provider.GetRequiredService<IOptions<TenantResolutionOptions<string>>>(),
            provider.GetRequiredService<TenantResolutionMetrics>(),
            provider.GetRequiredService<IOptions<TenantRequestMetricsOptions<string>>>(),
            provider.GetRequiredService<ILoggerFactory>());
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
        context.Request.Headers["X-Tenant-Id"] = "initech";
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RequireTenantAttribute()), "required"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden, "validators may exist, so not found must not be told apart");
    }

    [Fact]
    public async Task TheRequestsLogs_HaveATenantIdScope_AndTheMiddlewaresHaveStableEventIds()
    {
        var (app, logs) = await StartWithLogsAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme, Globex])
            .ValidateTenantAccess((_, t) => t.TenantId != "globex"));
        await using var _ = app;
        using var client = app.GetTestClient();

        (await Get(client, "acme", "/log")).Should().Be(HttpStatusCode.OK);
        (await Get(client, null, "/required")).Should().Be(HttpStatusCode.BadRequest);
        (await Get(client, "initech", "/required")).Should().Be(HttpStatusCode.Forbidden);
        (await Get(client, "globex", "/required")).Should().Be(HttpStatusCode.Forbidden);
        (await Get(client, null)).Should().Be(HttpStatusCode.OK);
        (await Get(client, "initech")).Should().Be(HttpStatusCode.OK);

        var fromEndpoint = logs.Entries.Should().ContainSingle(e => e.Message == "inside the request").Subject;
        fromEndpoint.ScopeProperties.Should().ContainSingle(p => p.Key == "TenantId").Which.Value.Should().Be("acme");

        // The scope is Tenantry's, with that one property, as Tenantry.Pro's jobs and messages have it.
        var tenantScope = fromEndpoint.Scopes.Should().ContainSingle(s => s!.ToString() == "TenantId:acme").Subject;
        tenantScope.Should().BeAssignableTo<IReadOnlyList<KeyValuePair<string, object?>>>().Which.Should().ContainSingle();

        var resolved = logs.For(1001).Should().ContainSingle().Subject;
        resolved.Category.Should().Be("Tenantry.AspNetCore");
        resolved.Level.Should().Be(LogLevel.Debug);
        resolved.ScopeProperties.Should().ContainSingle(p => p.Key == "TenantId");

        logs.For(1002).Should().ContainSingle();
        logs.For(1003).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        logs.For(1004).Should().ContainSingle().Which.Message.Should().Contain("'initech'").And.Contain("403");
        logs.For(1005).Should().ContainSingle().Which.Message.Should().Contain("globex");
        logs.For(1006).Should().ContainSingle().Which.Message.Should().Contain("names no tenant");
        logs.Entries.Where(e => e.Category == "Tenantry.AspNetCore").Select(e => e.EventId.Id)
            .Should().OnlyContain(id => id >= 1001 && id <= 1008);
    }

    [Fact]
    public async Task AnInactiveTenant_IsRefusedLikeOneAnAccessValidatorRefuses()
    {
        var (app, _) = await StartWithLogsAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme, Globex])
            .ValidateTenantActivity(t => t.TenantId != "globex"));
        await using var _ = app;
        using var client = app.GetTestClient();

        (await Get(client, "acme", "/required")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "globex", "/required")).Should().Be(HttpStatusCode.Forbidden);
        (await client.GetStringAsync("/tenant", TestContext.Current.CancellationToken)).Should().Be("(none)");
    }

    [Fact]
    public async Task EveryRequest_IsCountedByResult()
    {
        await using var app = await StartAsync<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([Acme, Globex])
            .ValidateTenantAccess((_, t) => t.TenantId != "globex"));
        List<string> measurements = [];
        using MeterListener listener = new();
        var meterFactory = app.Services.GetRequiredService<IMeterFactory>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == "Tenantry.AspNetCore")
            {
                instrument.Name.Should().Be("tenantry.resolutions");
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagged = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
            lock (measurements)
            {
                measurements.Add($"{value} {tagged["tenantry.resolution.result"]} {tagged["tenantry.resolution.rejected"]}");
            }
        });
        listener.Start();
        using var client = app.GetTestClient();

        await Get(client, "acme");
        await Get(client, null);
        await Get(client, null, "/required");
        await Get(client, "initech", "/required");
        await Get(client, "globex");

        measurements.Should().Equal(
            "1 resolved False", "1 missing False", "1 missing True", "1 not_found True", "1 access_denied False");
    }

    private static bool Record(List<string> calls, string name, bool allow)
    {
        calls.Add(name);
        return allow;
    }

    private static async Task<HttpStatusCode> Get(HttpClient client, string? tenantId, string path = "/tenant")
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);

        if (tenantId is not null)
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<(WebApplication App, RecordingLoggerProvider Logs)> StartWithLogsAsync<TKey>(
        Action<ITenantBuilder<TKey>> configure,
        Action<IServiceCollection>? services = null,
        Action<WebApplication>? pipeline = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        RecordingLoggerProvider logs = new();
        var app = await StartAsync(configure, services, pipeline, logs);
        return (app, logs);
    }

    // GET /tenant and /required (RequireTenant) return the current tenant id or "(none)"; GET /log logs a message.
    private static async Task<WebApplication> StartAsync<TKey>(
        Action<ITenantBuilder<TKey>> configure,
        Action<IServiceCollection>? services = null,
        Action<WebApplication>? pipeline = null,
        RecordingLoggerProvider? logs = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
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
        (pipeline ?? (a => a.UseTenantry()))(app);

        static string Current(ITenantContext<TKey> context) => context.HasTenant ? context.CurrentTenantId!.ToString()! : "(none)";

        app.MapGet("/tenant", (ITenantContext<TKey> context) => Current(context));
        app.MapGet("/required", (ITenantContext<TKey> context) => Current(context)).RequireTenant();
        app.MapGet("/log", (ILogger<ResolutionTests> logger) => logger.LogInformation("inside the request"));
        app.MapGet("/scoped", (HttpContext http, ITenantContext<TKey> context) =>
        {
            var services = http.RequestServices.GetService<RequestServices>();
            return $"{Current(context)} {services?.Id} " +
                   $"{(ReferenceEquals(http.Items["resolver"], services) ? "same" : "other")} " +
                   $"{(ReferenceEquals(http.Items["validator"], services) ? "same" : "other")}";
        });

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private sealed class SlugStore : ITenantStore<Guid>
    {
        public static readonly Guid Acme = Guid.NewGuid();

        public static readonly List<string> Lookups = [];

        private static readonly TenantDescriptor<Guid> AcmeTenant = new() { TenantId = Acme, Name = "Acme" };

        public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<Guid>?>(tenantId == Acme ? AcmeTenant : null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<Guid>>>([AcmeTenant]);

        public ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
        {
            Lookups.Add(identifier);
            return ValueTask.FromResult<ITenantDescriptor<Guid>?>(identifier is "acme" or "app.acme.com" ? AcmeTenant : null);
        }
    }

    private sealed class CountingStore : ITenantStore<string>
    {
        public int Lookups { get; private set; }

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return ValueTask.FromResult<ITenantDescriptor<string>?>(tenantId == "acme" ? Acme : null);
        }

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([Acme]);
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class RequestServices
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ScopedResolver(RequestServices services) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            context.Items["resolver"] = services;
            return ValueTask.FromResult<string?>("acme");
        }
    }

    private sealed class ScopedValidator(RequestServices services) : ITenantAccessValidator<string>
    {
        public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<string> tenant, CancellationToken cancellationToken)
        {
            context.Items["validator"] = services;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class Membership(List<string> calls, string allowed)
    {
        public bool Allows(ITenantDescriptor<string> tenant)
        {
            calls.Add("membership");
            return tenant.TenantId == allowed;
        }
    }

    private sealed class MembershipValidator(Membership membership) : ITenantAccessValidator<string>
    {
        public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<string> tenant, CancellationToken cancellationToken) =>
            ValueTask.FromResult(membership.Allows(tenant));
    }

    private sealed class NoUser(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "NoUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsPrincipal user = new(new ClaimsIdentity([new Claim("tenant_id", "acme")], Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Name)));
        }
    }
}
