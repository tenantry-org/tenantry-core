// What the documentation's code blocks take for granted. In a Program.cs: `builder`, `app`, `args` and a connection
// string. Inside AddTenantry's lambda: `tenant` (scripts/doc-snippets.cs wraps such blocks in WithTenant). In a
// fragment of a worker or a handler: the scope factory, a DbContext, a message and so on. And the application's own
// types that a block uses without declaring. A block's own declarations win over these, because its namespace is
// nested inside this one. Placeholders use Guid keys, as most of the docs do.
global using static DocSnippets.Ambient;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.AspNetCore;

namespace DocSnippets;

public static class Ambient
{
    public static string[] args = [];
    public static WebApplicationBuilder builder = null!;
    public static WebApplication app = null!;
    public static IServiceCollection services = null!;
    public static IServiceProvider sp = null!;
    public static IServiceProvider serviceProvider = null!;
    public static CancellationToken ct, cancellationToken;
    public static string connectionString = "";
    public static ILogger logger = null!;

    public static TenantList tenants = new();
    public static ITenantDescriptor<Guid> tenant = null!, acme = null!, globex = null!;
    public static Guid id, tenantId, dequeuedId, acmeId, globexId;
    public static ITenantContextSetter<Guid> tenantContext = null!;
    public static ITenantScope<Guid> scope = null!;
    public static ITenantScopeFactory<Guid> scopes = null!;
    public static ITenantActivity<Guid> activity = null!;
    public static AppDbContext db = null!;
    public static ModelBuilder modelBuilder = null!;
    public static Message message = null!;
    public static Queue<Guid> queue = new();

    public static void WithTenant<TKey>(Action<ITenantBuilder<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        builder.Services.AddTenantry(configure);

    public static string Handler() => "";
    public static bool IsFromTrustedIp(HttpContext http) => true;
    public static bool IsInternal(HttpContext http) => true;
    public static Task ProcessAsync(AppDbContext db, CancellationToken ct) => Task.CompletedTask;
    public static Task HandleAsync(ITenantScope<Guid> scope, CancellationToken ct) => Task.CompletedTask;
    public static Task HandleAsync(ITenantScope<Guid> scope, Message message, CancellationToken ct) =>
        Task.CompletedTask;
}

/// <summary>A list of tenants for either key type, which also reads like a tenant lookup.</summary>
public sealed class TenantList : List<ITenantDescriptor<Guid>>, IEnumerable<ITenantDescriptor<string>>,
    ITenantLookup<Guid>
{
    IEnumerator<ITenantDescriptor<string>> IEnumerable<ITenantDescriptor<string>>.GetEnumerator() =>
        Enumerable.Empty<ITenantDescriptor<string>>().GetEnumerator();

    public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        ValueTask.FromResult<ITenantDescriptor<Guid>?>(null);

    public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<Guid>>>(this);

    public ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken ct = default) =>
        ValueTask.FromResult<ITenantDescriptor<Guid>?>(null);
}

public sealed record Message(Guid TenantId, string Description);

public sealed class ChatHub : Microsoft.AspNetCore.SignalR.Hub;

/// <summary>The application's entry point, as <c>WebApplicationFactory&lt;Program&gt;</c> names it.</summary>
public partial class Program;

public sealed class Entitlements
{
    public Task<bool> CanAccessAsync<TKey>(ClaimsPrincipal user, TKey tenantId, CancellationToken ct) => Task.FromResult(true);
}

public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;
}

public class Order : TenantEntity<Guid>
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>An application's own tenant type, with what it knows about each tenant.</summary>
public class AppTenant : ITenantDescriptor<Guid>
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? CustomDomain { get; set; }
    public string Plan { get; set; } = "";
    public string Region { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public bool IsSuspended { get; set; }
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? SignInScheme { get; set; }
}

/// <summary>The catalog database, which lists the tenants.</summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<AppTenant> Tenants => Set<AppTenant>();
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
}

/// <summary>A context for maintenance code that writes across tenants.</summary>
public class MaintenanceDbContext(DbContextOptions<MaintenanceDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class EfCoreTenantStore : ITenantStore<Guid>
{
    public ValueTask<ITenantDescriptor<Guid>?> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        ValueTask.FromResult<ITenantDescriptor<Guid>?>(null);

    public ValueTask<IReadOnlyList<ITenantDescriptor<Guid>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<Guid>>>([]);
}

public sealed class AppTenantStore : ITenantStore<string>
{
    public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken ct = default) =>
        ValueTask.FromResult<ITenantDescriptor<string>?>(null);

    public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([]);
}

public sealed class CookieTenantResolver : ITenantResolver
{
    public ValueTask<string?> ResolveAsync(Microsoft.AspNetCore.Http.HttpContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(context.Request.Cookies["tenant"]);
}

public sealed class MembershipValidator : ITenantAccessValidator<Guid>
{
    public ValueTask<bool> ValidateAsync(Microsoft.AspNetCore.Http.HttpContext context, ITenantDescriptor<Guid> tenant, CancellationToken ct) =>
        ValueTask.FromResult(true);
}

/// <summary>A secrets client, as Azure Key Vault's or AWS Secrets Manager's.</summary>
public sealed class SecretClient
{
    public Task<string> GetSecretAsync(string name, CancellationToken ct = default) => Task.FromResult("");
}

/// <summary>A connection-string provider over a secrets client.</summary>
public sealed class VaultConnectionStrings(SecretClient secrets) : ITenantConnectionStringProvider<string>
{
    public bool CanGetSynchronously => false;

    public string Get(ITenantDescriptor<string> tenant) => throw new NotSupportedException();

    public async ValueTask<string> GetAsync(ITenantDescriptor<string> tenant, CancellationToken ct = default) =>
        await secrets.GetSecretAsync($"connstr-{tenant.TenantId}", ct);
}

/// <summary>A decorator that logs each connection string read.</summary>
public sealed class LoggingConnectionStrings(ITenantConnectionStringProvider<string> inner) : ITenantConnectionStringProvider<string>
{
    public bool CanGetSynchronously => inner.CanGetSynchronously;

    public string Get(ITenantDescriptor<string> tenant) => inner.Get(tenant);

    public ValueTask<string> GetAsync(ITenantDescriptor<string> tenant, CancellationToken ct = default) => inner.GetAsync(tenant, ct);
}
