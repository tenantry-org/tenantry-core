using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Tenantry.Tests.Shared;

namespace Tenantry.Http.Tests;

/// <summary>
/// <c>UseTenantry()</c> on an HttpClient: which requests carry the current tenant, in which form, and the
/// registrations it refuses.
/// </summary>
public sealed class TenantPropagationHandlerTests
{
    private static readonly Uri Billing = new("https://billing.internal/");

    [Fact]
    public async Task ARequestAsATenant_CarriesItsId_FormattedWithTheInvariantCulture()
    {
        var (provider, recorder) = Build<int>(client => client.BaseAddress = Billing);
        await using var _ = provider;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;

        try
        {
            using (MakeCurrent(provider, -5))
            {
                await Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        recorder.TenantHeaders.Should().Equal("-5");
    }

    [Fact]
    public async Task ARequestWithoutATenant_GoesWithoutTheHeader()
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;

        await Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken);

        recorder.TenantHeaders.Should().Equal((string?)null);
    }

    [Fact]
    public async Task AHeaderNamingAnotherTenant_IsRefused_WhileATenantIsCurrent()
    {
        // A forwarded or stale header must not call the service as another tenant.
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;
        using HttpRequestMessage request = new(HttpMethod.Get, "/invoices");
        request.Headers.Add(TenantPropagation.HeaderName, "globex");

        using (MakeCurrent(provider, "acme"))
        {
            // The assertion awaits the call, so the request is not used after it is disposed.
            // ReSharper disable once AccessToDisposedClosure
            await FluentActions.Awaiting(() => Client(provider).SendAsync(request, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*'globex'*current tenant is 'acme'*");
        }

        recorder.TenantHeaders.Should().BeEmpty();
    }

    [Fact]
    public async Task AHeaderTheCallerSet_IsSent_WhenItNamesTheCurrentTenant()
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;

        using (MakeCurrent(provider, "acme"))
        {
            using HttpRequestMessage same = new(HttpMethod.Get, "/invoices");
            same.Headers.Add(TenantPropagation.HeaderName, "acme");
            await Client(provider).SendAsync(same, TestContext.Current.CancellationToken);
        }

        recorder.TenantHeaders.Should().Equal("acme");
    }

    [Fact]
    public async Task AHeaderTheCallerSet_IsRefused_WhenNoTenantIsCurrent()
    {
        // A header an untrusted client sent, forwarded by header propagation from an endpoint that allows a missing
        // tenant, must not call the service as the tenant it names.
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;
        using HttpRequestMessage request = new(HttpMethod.Get, "/invoices");
        request.Headers.Add(TenantPropagation.HeaderName, "globex");

        // The assertion awaits the call, so the request is not used after it is disposed.
        // ReSharper disable once AccessToDisposedClosure
        await FluentActions.Awaiting(() => Client(provider).SendAsync(request, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*'globex'*no tenant is current*MakeCurrent*");

        recorder.TenantHeaders.Should().BeEmpty();
    }

    [Fact]
    public async Task AHeaderTheCallerSet_IsSentUnchecked_ToAnotherService()
    {
        // The header goes only to the client's own service; a request elsewhere is the caller's.
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;
        using HttpRequestMessage request = new(HttpMethod.Get, "https://payments.example.com/charge");
        request.Headers.Add(TenantPropagation.HeaderName, "globex");

        await Client(provider).SendAsync(request, TestContext.Current.CancellationToken);

        recorder.TenantHeaders.Should().Equal("globex");
    }

    [Fact]
    public async Task WithABaseAddress_OnlyRequestsToItsSchemeHostAndPort_CarryTheTenant()
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;
        var client = Client(provider);

        using (MakeCurrent(provider, "acme"))
        {
            await client.GetAsync("/invoices", TestContext.Current.CancellationToken);
            await client.GetAsync("https://BILLING.internal/other", TestContext.Current.CancellationToken);
            await client.GetAsync("https://billing.internal:8443/invoices", TestContext.Current.CancellationToken);
            await client.GetAsync("http://billing.internal/invoices", TestContext.Current.CancellationToken);
            await client.GetAsync("https://payments.example.com/charge", TestContext.Current.CancellationToken);
        }

        recorder.TenantHeaders.Should().Equal("acme", "acme", null, null, null);
    }

    [Fact]
    public async Task ABaseAddressSetAfterUseTenantry_StillLimitsTheTenantToIt()
    {
        var (provider, recorder) = Build<string>(configureAfter: client => client.BaseAddress = Billing);
        await using var _ = provider;

        using (MakeCurrent(provider, "acme"))
        {
            await Client(provider).GetAsync("https://payments.example.com/charge", TestContext.Current.CancellationToken);
            await Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken);
        }

        recorder.TenantHeaders.Should().Equal(null, "acme");
    }

    [Fact]
    public void AClientWithoutABaseAddress_IsRefusedWhenItIsCreated_WithoutAHost()
    {
        var (provider, _) = Build<string>();
        using var _ = provider;

        FluentActions.Invoking(() => Client(provider))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The HTTP client 'billing' calls UseTenantry(), but its registration sets no absolute BaseAddress*UseTenantry(address)*");
    }

    [Fact]
    public async Task AClientWithoutABaseAddress_StopsTheHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([]));
        builder.Services.AddHttpClient("billing").UseTenantry();
        using var host = builder.Build();

        await host.Awaiting(h => h.StartAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The HTTP client 'billing' calls UseTenantry(), but its registration sets no absolute BaseAddress*");
    }

    [Fact]
    public async Task TheHostStarts_WithClientsThatCanSendTheTenant_WhereverTheirAddressIsSet()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([]));
        builder.Services.AddHttpClient("billing").UseTenantry().ConfigureHttpClient(c => c.BaseAddress = Billing);
        builder.Services.AddHttpClient("inventory").UseTenantry(new Uri("https://inventory.internal"));
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AServiceAddressGivenToUseTenantry_LimitsTheTenantToIt_WithoutABaseAddress()
    {
        var (provider, recorder) = Build<string>(serviceAddress: new Uri("https://inventory.internal"));
        await using var _ = provider;

        using (MakeCurrent(provider, "acme"))
        {
            await Client(provider).GetAsync("https://payments.example.com/charge", TestContext.Current.CancellationToken);
            await Client(provider).GetAsync("https://inventory.internal/item", TestContext.Current.CancellationToken);
        }

        recorder.TenantHeaders.Should().Equal(null, "acme");
    }

    [Fact]
    public void ARelativeServiceAddress_IsRefused()
    {
        ServiceCollection services = new();

        FluentActions.Invoking(() => services.AddHttpClient("inventory").UseTenantry(new Uri("/inventory", UriKind.Relative)))
            .Should().Throw<ArgumentException>().WithMessage("*must be absolute*");
    }

    [Fact]
    public async Task ASynchronousSend_CarriesTheTenantToo()
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;

        using (MakeCurrent(provider, "acme"))
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "/invoices");
            Client(provider).Send(request, TestContext.Current.CancellationToken).Dispose();
        }

        recorder.TenantHeaders.Should().Equal("acme");
    }

    [Theory]
    [InlineData("café")]
    [InlineData("line\nbreak")]
    [InlineData(" acme")]
    [InlineData("acme ")]
    public async Task ATenantIdAHeaderCannotCarry_FailsTheRequest_RatherThanSendingSomethingElse(string tenantId)
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing);
        await using var _ = provider;

        using (MakeCurrent(provider, tenantId))
        {
            await FluentActions.Awaiting(() => Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be sent in the tenantry-tenant-id header*");
        }

        recorder.TenantHeaders.Should().BeEmpty();
    }

    [Fact]
    public void UseTenantry_AddsNothingToTheClientsHttpClientActions_WhichGrpcClientsRefuse()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([]));
        services.AddHttpClient("inventory").UseTenantry(new Uri("https://inventory.internal"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("inventory").HttpClientActions.Should().BeEmpty();
    }

    [Fact]
    public void UseTenantry_RefusesConfigureHttpClientDefaults_WhichConfiguresEveryClient()
    {
        ServiceCollection services = new();

        FluentActions.Invoking(() => services.ConfigureHttpClientDefaults(client => client.UseTenantry()))
            .Should().Throw<InvalidOperationException>().WithMessage("*cannot go in ConfigureHttpClientDefaults*");
    }

    [Fact]
    public async Task AClientThatUsesTenantry_WithoutAddTenantry_FailsWhenItIsCreated()
    {
        ServiceCollection services = new();
        services.AddHttpClient("billing", c => c.BaseAddress = Billing).UseTenantry();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHttpClientFactory>().Invoking(f => f.CreateClient("billing"))
            .Should().Throw<InvalidOperationException>().WithMessage("*Tenantry is not registered*AddTenantry<TKey>*");
    }

    [Fact]
    public async Task AHeaderSetByAHandlerAddedAfterUseTenantry_IsNotChecked()
    {
        // Handlers run in the order they are added, so Tenantry's has run when this one sets the header.
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing, handlerAfter: () => new SetsTenantHeader());
        await using var _ = provider;

        await Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken);

        recorder.TenantHeaders.Should().Equal("globex");
    }

    [Fact]
    public async Task AHeaderSetByAHandlerAddedBeforeUseTenantry_IsRefused_WhenNoTenantIsCurrent()
    {
        var (provider, recorder) = Build<string>(client => client.BaseAddress = Billing, handlerBefore: () => new SetsTenantHeader());
        await using var _ = provider;

        await FluentActions.Awaiting(() => Client(provider).GetAsync("/invoices", TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*'globex'*no tenant is current*");

        recorder.TenantHeaders.Should().BeEmpty();
    }

    private static (ServiceProvider Provider, Recorder Recorder) Build<TKey>(
        Action<HttpClient>? configure = null,
        Action<HttpClient>? configureAfter = null,
        Uri? serviceAddress = null,
        Func<DelegatingHandler>? handlerBefore = null,
        Func<DelegatingHandler>? handlerAfter = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        Recorder recorder = new();
        ServiceCollection services = new();
        services.AddTenantry<TKey>(tenant => tenant.UseInMemoryStore([]));
        var client = services.AddHttpClient("billing", c => configure?.Invoke(c));

        if (handlerBefore is not null)
            client.AddHttpMessageHandler(handlerBefore);

        client.UseTenantry(serviceAddress);

        if (handlerAfter is not null)
            client.AddHttpMessageHandler(handlerAfter);

        client.ConfigurePrimaryHttpMessageHandler(() => recorder);

        if (configureAfter is not null)
            client.ConfigureHttpClient(configureAfter);

        return (services.BuildServiceProvider(Conformance.ProviderOptions), recorder);
    }

    private static HttpClient Client(IServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("billing");

    private static IDisposable MakeCurrent<TKey>(IServiceProvider provider, TKey tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        provider.GetRequiredService<ITenantContextSetter<TKey>>()
            .MakeCurrent(new TenantDescriptor<TKey> { TenantId = tenantId, Name = "Tenant" });

    // Stands in for header propagation: sets the header an incoming request carried.
    private sealed class SetsTenantHeader : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(TenantPropagation.HeaderName, "globex");
            return base.SendAsync(request, cancellationToken);
        }
    }

    // The primary handler: answers 200 and records the header each request arrived with.
    private sealed class Recorder : HttpMessageHandler
    {
        public List<string?> TenantHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TenantHeaders.Add(request.Headers.TryGetValues(TenantPropagation.HeaderName, out var values)
                ? string.Join(",", values)
                : null);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
