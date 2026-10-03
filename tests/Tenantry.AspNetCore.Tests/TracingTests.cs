using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Activity listeners see every activity in the process, so these tests run on their own.
/// </summary>
[CollectionDefinition(nameof(TracingCollection), DisableParallelization = true)]
public sealed class TracingCollection;

[Collection(nameof(TracingCollection))]
public sealed class TracingTests
{
    [Fact]
    public async Task TheRequestsSpan_IsTaggedWithTheTenant_AndResolutionHasASpanOfItsOwn()
    {
        ConcurrentQueue<Activity> stopped = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name is "Microsoft.AspNetCore" or "Tenantry.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry<int>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore([new TenantDescriptor<int> { TenantId = -1234567, Name = "Acme" }]));
        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        // A culture whose negative sign is not '-': the tag is formatted with the invariant culture regardless.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var (previous, previousDefault) = (CultureInfo.CurrentCulture, CultureInfo.DefaultThreadCurrentCulture);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;

        try
        {
            using (HttpRequestMessage request = new(HttpMethod.Get, "/tenant"))
            {
                request.Headers.Add("X-Tenant-Id", "-1234567");
                (await client.SendAsync(request, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            }

            (await client.GetAsync("/tenant", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.DefaultThreadCurrentCulture) = (previous, previousDefault);
        }

        // In the order the requests were sent: the test server can stop a request's span after the next one's.
        var requests = stopped.Where(a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn").OrderBy(a => a.StartTimeUtc).ToList();
        var resolutions = stopped.Where(a => a.Source.Name == "Tenantry.AspNetCore").OrderBy(a => a.StartTimeUtc).ToList();
        requests.Should().HaveCount(2);
        resolutions.Should().HaveCount(2);

        // The tenant id is formatted with the invariant culture, as jobs and messages carry it.
        requests[0].GetTagItem("tenant.id").Should().Be("-1234567");
        requests[1].GetTagItem("tenant.id").Should().BeNull();

        resolutions.Select(a => a.OperationName).Should().AllBe("Tenantry.ResolveTenant");
        resolutions[0].ParentSpanId.Should().Be(requests[0].SpanId);
        resolutions[0].GetTagItem("tenantry.resolution.result").Should().Be("resolved");
        resolutions[0].GetTagItem("tenant.id").Should().Be("-1234567");
        resolutions[1].GetTagItem("tenantry.resolution.result").Should().Be("missing");
        resolutions[1].GetTagItem("tenant.id").Should().BeNull();
    }
}
