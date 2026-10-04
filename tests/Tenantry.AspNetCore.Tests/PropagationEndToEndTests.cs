using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Tenantry.Tests.Shared;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// One service calls another as a tenant: Tenantry.Http sends the tenant, and the receiving service resolves it with
/// <c>ResolveFromPropagationHeader(...)</c>, by id, from a store that maps its identifiers to slugs only.
/// </summary>
public sealed class PropagationEndToEndTests
{
    private static readonly Guid AcmeId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Fact]
    public async Task TheCalledService_ResolvesTheCallersTenant_ById()
    {
        await using var service = await StartServiceAsync();
        await using var caller = BuildCaller(service);

        using (caller.GetRequiredService<ITenantContextSetter<Guid>>().MakeCurrent(SlugStore.Acme))
        {
            var answer = await caller.GetRequiredService<IHttpClientFactory>().CreateClient("service")
                .GetStringAsync("/tenant", TestContext.Current.CancellationToken);

            answer.Should().Be("Acme");
        }
    }

    [Fact]
    public async Task ACallWithoutATenant_IsRejected_ByAnEndpointThatRequiresOne()
    {
        await using var service = await StartServiceAsync();
        await using var caller = BuildCaller(service);

        var response = await caller.GetRequiredService<IHttpClientFactory>().CreateClient("service")
            .GetAsync("/tenant", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public async Task AHeaderThatIsNotATenantId_FindsNoTenant_EvenIfTheStoreKnowsItAsAnIdentifier(string value)
    {
        await using var service = await StartServiceAsync();
        using var client = service.GetTestClient();
        client.DefaultRequestHeaders.Add(TenantPropagation.HeaderName, value);

        var response = await client.GetAsync("/tenant", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheCalledService_PassesConformance()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = Conformance.ProviderOptions.ValidateScopes;
            options.ValidateOnBuild = Conformance.ProviderOptions.ValidateOnBuild;
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry<Guid>(tenant => tenant
            .ResolveFromPropagationHeader(_ => true)   // the test's callers are all its own services
            .UseStore<SlugStore>()
            .AddHttpPropagation());
        builder.Services.AddHttpClient("next", client => client.BaseAddress = new Uri("https://next.internal")).UseTenantry();

        await using var app = builder.Build();
        app.UseTenantry();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);
        }

        await Conformance.StartAndStopAsync(app);
    }

    private static async Task<WebApplication> StartServiceAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry<Guid>(tenant => tenant.ResolveFromPropagationHeader(_ => true).UseStore<SlugStore>());   // its callers are the test

        var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<Guid> tenant) => tenant.CurrentTenant!.Name).RequireTenant();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static ServiceProvider BuildCaller(WebApplication service)
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant.UseInMemoryStore([SlugStore.Acme]).AddHttpPropagation());
        services.AddHttpClient("service", client => client.BaseAddress = new Uri("http://localhost/"))
            .UseTenantry()
            .ConfigurePrimaryHttpMessageHandler(() => service.GetTestServer().CreateHandler());
        return services.BuildServiceProvider(Conformance.ProviderOptions);
    }

    // A store that maps identifiers to slugs only, as a store for subdomains does: an id is not an identifier here.
    private sealed class SlugStore : ITenantStore<Guid>
    {
        public static readonly ITenantDescriptor<Guid> Acme = new TenantDescriptor<Guid> { TenantId = AcmeId, Name = "Acme" };

        public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tenantId == AcmeId ? Acme : null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<Guid>>>([Acme]);

        public ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(identifier == "acme" ? Acme : null);
    }
}
