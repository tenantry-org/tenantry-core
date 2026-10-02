# Testing

Tenantry's services run in memory, so tests can use the real ones: register Tenantry as the application does, with
an in-memory store, and make a tenant current the way the application does. Such a test checks what a substitute
for `ITenantContext<TKey>` cannot: that the code runs inside a tenant's scope, that the scoped services it resolves
see the tenant, and that EF Core keeps each tenant's data to itself.

The examples use xUnit v3; the approach is the same with any test framework.

## Code that reads the current tenant

Build a service provider with `AddTenantry` and the services under test, then resolve them from a tenant's scope:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Xunit;

public sealed class InvoiceNumbers(ITenantContext<string> tenantContext)
{
    public string For(int sequence) => $"{tenantContext.CurrentTenantId}-{sequence:D4}";
}

public class InvoiceNumbersTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    [Fact]
    public async Task ANumberStartsWithTheTenantId()
    {
        await using var services = new ServiceCollection()
            .AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]))
            .AddScoped<InvoiceNumbers>()
            .BuildServiceProvider(validateScopes: true);

        await using var scope = services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Acme);

        var numbers = scope.ServiceProvider.GetRequiredService<InvoiceNumbers>();
        Assert.Equal("acme-0042", numbers.For(42));
    }
}
```

`validateScopes: true` makes the provider refuse a scoped service resolved from the root, as the host does in
Development. Code that receives only a tenant id is tested the same way with `RunInScopeAsync(id, …)`, and code
that already has its services with `ITenantContextSetter<TKey>.Use(tenant)`
([Non-HTTP hosts](non-http-hosts.md#running-work-as-a-tenant)). Background work is tested the same way too: run the
work for one tenant inside that tenant's scope.

Make the tenant current in the test method itself. The current tenant is held in an `AsyncLocal`, so a tenant made
current inside an `async` setup method (an `InitializeAsync` that awaits, say) is no longer current when that method
returns, and the test runs with no tenant.

## Requests: `WebApplicationFactory`

`WebApplicationFactory<Program>`, from Microsoft.AspNetCore.Mvc.Testing, runs the application in memory. On .NET 8 and
9, the `Program` class that top-level statements produce is internal, so add `public partial class Program;` at the
end of `Program.cs` for the tests to name it (.NET 10 makes it public). Send the tenant the way the application
resolves it, here in a header:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class OrdersApiTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ARequestRunsAsTheTenantItNames()
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await client.GetAsync("/orders", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutATenantIsRejected()
    {
        // The endpoint requires a tenant.
        var response = await app.CreateClient().GetAsync("/orders", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

The [response for each rejection](aspnetcore-integration.md#status-codes) is the same as in production: 400 without
a tenant, 404 for an id the store does not have, and 403 for a tenant an access validator refuses. With access
validators configured, an id the store does not have gets 403 too, so a caller cannot probe for tenants. For subdomain or
host resolution, give the client the tenant's host instead of a header:
`app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://acme.example.com") })`.
For claim-based access validation, the client needs a signed-in caller: the
[`SecureApi` sample's tests](../tests/Tenantry.IntegrationTests/SecureApiTests.cs) sign tokens with a key set
for the test.

To test with tenants of your own rather than the ones the application's store holds, replace the store:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Xunit;

public class TestTenantsApiTests(WebApplicationFactory<Program> app) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app = app.WithWebHostBuilder(host =>
        host.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantStore<string>>();
            services.AddSingleton<ITenantStore<string>>(new InMemoryTenantStore<string>(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" },
            ]));
        }));

    // The application has no access validators: with one, an unknown tenant gets 403.
    [Fact]
    public async Task AnUnknownTenantIsNotFound()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "initech");

        var response = await client.GetAsync("/orders", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

## EF Core isolation

Test isolation against a relational database. SQLite in memory is fast, and Tenantry's own unit tests check its
isolation on it; to test what depends on your database (a database per tenant, your migrations), run the same
tests against the database you deploy on, in a container ([Testcontainers](https://dotnet.testcontainers.org/), for
example). EF Core's documentation advises against its in-memory provider for tests: it is not a relational
database.

With these entities:

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class Order : TenantEntity<string>
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
```

each test writes as one tenant and reads as another:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore;
using Xunit;

public sealed class OrderIsolationTests : IAsyncLifetime
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    // An in-memory SQLite database lasts as long as its connection is open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _services = new ServiceCollection()
            .AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme, Globex]))
            .AddDbContext<AppDbContext>(options => options.UseSqlite(_connection).UseTenantry())
            .BuildServiceProvider(validateScopes: true);

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ATenantReadsOnlyItsOwnOrders()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddOrderAsync(Acme, "A-1", ct);
        await AddOrderAsync(Globex, "G-1", ct);

        await using var scope = _services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Acme);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(["A-1"], await db.Orders.Select(o => o.Reference).ToListAsync(ct));
    }

    [Fact]
    public async Task AnOrderForAnotherTenantIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Acme);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Orders.Add(new Order { TenantId = "globex", Reference = "G-2" });

        var error = await Assert.ThrowsAsync<TenantIsolationViolationException>(() => db.SaveChangesAsync(ct));
        Assert.Equal(TenantIsolationViolationKind.EntityWrite, error.Kind);
    }

    [Fact]
    public async Task WithoutATenantNothingIsReadOrWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddOrderAsync(Acme, "A-1", ct);

        // A plain scope: no tenant is current.
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Orders.ToListAsync(ct));
        db.Orders.Add(new Order { Reference = "X-1" });
        await Assert.ThrowsAsync<TenantNotResolvedException>(() => db.SaveChangesAsync(ct));
    }

    private async Task AddOrderAsync(ITenantDescriptor<string> tenant, string reference, CancellationToken ct)
    {
        await using var scope = _services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order { Reference = reference });
        await db.SaveChangesAsync(ct);
    }
}
```

The same tests run against your application's own registration if you build the provider from it, for example
with an extension method that both `Program.cs` and the tests call.

## Cached tenants and time

`CacheTenants` reads the time from a registered `TimeProvider`. To test what happens when a tenant's entry expires,
register a `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) as the `TimeProvider` and advance it.

## Tenantry.Pro

Tenantry.Pro checks its licence key when the application starts, in tests too. Its
[testing page](https://tenantry.dev/docs/pro/testing) covers the key in tests and CI, and testing provisioning,
migrations, jobs and messages.

## See also

- [Non-HTTP hosts](non-http-hosts.md): scopes, `RunInScopeAsync` and `ITenantContextSetter`.
- [EF Core integration](efcore-integration.md): what the query filter and the `SaveChanges` interceptor check.
- [Tenant stores](tenant-stores.md#caching): caching tenants, and removing a tenant from the cache.
