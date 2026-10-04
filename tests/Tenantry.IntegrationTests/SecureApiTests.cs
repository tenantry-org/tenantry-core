using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Tenantry.Samples.SecureApi;

namespace Tenantry.IntegrationTests;

/// <summary>
/// Hosts the production-shaped SecureApi sample in-process and checks the request-level guarantees:
/// authentication, caller-to-tenant validation, required tenants and per-tenant data isolation.
/// </summary>
public sealed class SecureApiTests : IAsyncLifetime
{
    private const string SigningKey = "integration-test-signing-key-0123456789abcdef";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"secure-api-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;

    private static readonly AuthSettings Auth = new()
    {
        Issuer = "https://login.example.com",
        Audience = "notes-api",
        SigningKey = SigningKey,
    };

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("Auth:SigningKey", SigningKey);
            host.UseSetting("ConnectionStrings:Notes", $"Data Source={_databasePath}");
        });

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task AnonymousRequest_IsRejectedWith401()
    {
        var response = await Client(token: null, tenant: "acme").GetAsync("/notes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SelectingATenantTheCallerDoesNotBelongTo_IsRejectedWith403()
    {
        var response = await Client(Token("alice", "acme"), tenant: "globex").GetAsync("/notes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequestWithoutATenant_IsRejectedWith400()
    {
        var response = await Client(Token("alice", "acme"), tenant: null).GetAsync("/notes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownTenant_GetsTheSameResponseAsATenantTheCallerMayNotUse()
    {
        // The access validator (the token's tenant claims) makes a tenant that does not exist look like one the
        // caller may not use, so a caller cannot find out which tenants exist.
        var unknown = await Client(Token("alice", "acme", "initech"), tenant: "initech").GetAsync("/notes", TestContext.Current.CancellationToken);
        var notTheirs = await Client(Token("alice", "acme"), tenant: "globex").GetAsync("/notes", TestContext.Current.CancellationToken);

        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        notTheirs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HealthEndpoint_NeedsNeitherAuthenticationNorTenant()
    {
        var response = await Client(token: null, tenant: null).GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MemberOfSeveralTenants_SeesOnlyTheSelectedTenantsNotes()
    {
        var token = Token("bob", "acme", "globex");
        var acme = Client(token, "acme");
        var globex = Client(token, "globex");

        (await acme.PostAsJsonAsync("/notes", new CreateNote("acme plan"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await globex.PostAsJsonAsync("/notes", new CreateNote("globex plan"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await acme.GetFromJsonAsync<List<NoteResponse>>("/notes", cancellationToken: TestContext.Current.CancellationToken))!.Select(n => n.Text).Should().Equal("acme plan");
        (await globex.GetFromJsonAsync<List<NoteResponse>>("/notes", cancellationToken: TestContext.Current.CancellationToken))!.Select(n => n.Text).Should().Equal("globex plan");
    }

    [Fact]
    public async Task DeletingAnotherTenantsNote_FindsNothingAndLeavesItInPlace()
    {
        var created = await (await Client(Token("alice", "acme"), "acme")
            .PostAsJsonAsync("/notes", new CreateNote("acme secret"), cancellationToken: TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<NoteResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response = await Client(Token("mallory", "globex"), "globex").DeleteAsync($"/notes/{created!.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client(Token("alice", "acme"), "acme").GetFromJsonAsync<List<NoteResponse>>("/notes", cancellationToken: TestContext.Current.CancellationToken))!
            .Select(n => n.Id).Should().Contain(created.Id);
    }

    [Fact]
    public async Task WritingOutsideARequestTenantScope_IsRejected()
    {
        // e.g. a background job that forgot to open a tenant scope
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        db.Notes.Add(new Note { Text = "no tenant" });

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantNotResolvedException>();
    }

    private HttpClient Client(string? token, string? tenant)
    {
        var client = _factory.CreateClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        }

        return client;
    }

    private static string Token(string subject, params string[] tenants) =>
        Auth.IssueDevelopmentToken(subject, tenants);
}
