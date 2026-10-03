using System.Diagnostics.Metrics;
using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// <c>tenant.TagRequestMetrics()</c>, read through ASP.NET Core's own <c>http.server.request.duration</c>.
/// </summary>
public sealed class RequestMetricsTests
{
    private static readonly TenantDescriptor<string>[] Tenants =
    [
        new() { TenantId = "acme", Name = "Acme" },
        new() { TenantId = "globex", Name = "Globex" },
    ];

    [Fact]
    public async Task ATenantsRequest_IsTaggedWithTheTenantId()
    {
        await using var host = await MetricsHost<string>.StartAsync(Tenants, tenant => tenant.TagRequestMetrics(), app =>
            app.MapGet("/tenant", (ITenantContext<string> tenant) => tenant.CurrentTenantId));

        (await host.GetAsync("/tenant", "acme")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await host.Recorder.NextAsync()).Should().ContainSingle(tag => tag.Key == "tenant.id").Which.Value.Should().Be("acme");
    }

    [Fact]
    public async Task ARequestWithoutATenant_HasNoTenantTag()
    {
        await using var host = await MetricsHost<string>.StartAsync(Tenants, tenant => tenant.TagRequestMetrics(), app =>
            app.MapGet("/public", () => "ok").AllowMissingTenant());

        (await host.GetAsync("/public", tenantId: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tags = await host.Recorder.NextAsync();
        tags.Should().NotContain(tag => tag.Key == "tenant.id");
        tags.Should().Contain(tag => tag.Key == "http.route" && Equals(tag.Value, "/public"));
    }

    [Fact]
    public async Task TheTagsValue_ComesFromTheFunction_AndNullLeavesItOff()
    {
        await using var host = await MetricsHost<string>.StartAsync(
            Tenants,
            tenant => tenant.TagRequestMetrics(t => t.TenantId == "acme" ? "watched" : null),
            app => app.MapGet("/tenant", () => "ok"));

        await host.GetAsync("/tenant", "acme");
        var acme = await host.Recorder.NextAsync();
        await host.GetAsync("/tenant", "globex");
        var globex = await host.Recorder.NextAsync();

        acme.Should().ContainSingle(tag => tag.Key == "tenant.id").Which.Value.Should().Be("watched");
        globex.Should().NotContain(tag => tag.Key == "tenant.id");
    }

    [Fact]
    public async Task ARequestTheExceptionHandlerRunsAgain_IsTaggedOnce()
    {
        // UseExceptionHandler("/error") runs app.UseTenantry() again for the same request.
        await using var host = await MetricsHost<string>.StartAsync(
            Tenants,
            tenant => tenant.TagRequestMetrics(),
            app =>
            {
                app.MapGet("/boom", string () => throw new InvalidOperationException("boom"));
                app.MapGet("/error", () => Results.Problem());
            },
            exceptionHandler: "/error");

        (await host.GetAsync("/boom", "acme")).StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var tags = await host.Recorder.NextAsync();
        tags.Should().ContainSingle(tag => tag.Key == "tenant.id").Which.Value.Should().Be("acme");
        tags.Should().ContainSingle(tag => tag.Key == "http.response.status_code").Which.Value.Should().Be(500);
    }

    [Fact]
    public async Task AnIntKey_IsTaggedAsText()
    {
        await using var host = await MetricsHost<int>.StartAsync(
            [new TenantDescriptor<int> { TenantId = 42, Name = "Answer" }],
            tenant => tenant.TagRequestMetrics(),
            app => app.MapGet("/tenant", () => "ok"));

        await host.GetAsync("/tenant", "42");

        (await host.Recorder.NextAsync()).Should().ContainSingle(tag => tag.Key == "tenant.id").Which.Value.Should().Be("42");
    }

    [Fact]
    public async Task WithoutTagRequestMetrics_NoRequestIsTagged()
    {
        await using var host = await MetricsHost<string>.StartAsync(Tenants, _ => { }, app => app.MapGet("/tenant", () => "ok"));

        await host.GetAsync("/tenant", "acme");

        (await host.Recorder.NextAsync()).Should().NotContain(tag => tag.Key == "tenant.id");
    }

    [Fact]
    public void TagRequestMetrics_RefusesNoBuilder_AndReturnsTheBuilder()
    {
        FluentActions.Invoking(() => TenantryAspNetCoreTenantBuilderExtensions.TagRequestMetrics<string>(null!))
            .Should().Throw<ArgumentNullException>();

        new ServiceCollection().AddTenantry<string>(tenant => tenant.TagRequestMetrics().Should().BeSameAs(tenant));
    }

    /// <summary>A web application and the <c>http.server.request.duration</c> it records.</summary>
    private sealed class MetricsHost<TKey> : IAsyncDisposable
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private MetricsHost(WebApplication app, DurationRecorder recorder)
        {
            _app = app;
            _client = app.GetTestClient();
            Recorder = recorder;
        }

        public DurationRecorder Recorder { get; }

        public static async Task<MetricsHost<TKey>> StartAsync(
            IEnumerable<TenantDescriptor<TKey>> tenants,
            Action<ITenantBuilder<TKey>> configure,
            Action<WebApplication> map,
            string? exceptionHandler = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddTenantry<TKey>(tenant =>
            {
                tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore(tenants);
                configure(tenant);
            });

            var app = builder.Build();

            if (exceptionHandler is not null)
            {
                app.UseExceptionHandler(exceptionHandler);
            }

            app.UseTenantry();
            map(app);

            // Listen before the first request: ASP.NET Core tags only requests that start while the metric is listened to.
            var recorder = new DurationRecorder(app.Services.GetRequiredService<IMeterFactory>());
            await app.StartAsync(TestContext.Current.CancellationToken);

            return new MetricsHost<TKey>(app, recorder);
        }

        public async Task<HttpResponseMessage> GetAsync(string path, string? tenantId)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, path);

            if (tenantId is not null)
            {
                request.Headers.Add("X-Tenant-Id", tenantId);
            }

            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync(TestContext.Current.CancellationToken);
            await _app.DisposeAsync();
            Recorder.Dispose();
        }
    }

    /// <summary>
    /// The tags of each <c>http.server.request.duration</c> one application records. ASP.NET Core records it as the
    /// request ends, which can be after the client has the response, so <see cref="NextAsync"/> waits for it.
    /// </summary>
    private sealed class DurationRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Queue<KeyValuePair<string, object?>[]> _recorded = new();
        private readonly SemaphoreSlim _available = new(0);

        public DurationRecorder(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == "Microsoft.AspNetCore.Hosting" &&
                    instrument.Name == "http.server.request.duration")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            {
                lock (_recorded)
                {
                    _recorded.Enqueue(tags.ToArray());
                }

                _available.Release();
            });
            _listener.Start();
        }

        public async Task<KeyValuePair<string, object?>[]> NextAsync()
        {
            (await _available.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                .Should().BeTrue("ASP.NET Core records each request");

            lock (_recorded)
            {
                return _recorded.Dequeue();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
            _available.Dispose();
        }
    }
}
