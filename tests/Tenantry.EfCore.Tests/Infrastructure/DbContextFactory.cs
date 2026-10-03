using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;

namespace Tenantry.EfCore.Tests.Infrastructure;

/// <summary>
/// Creates in-memory SQLite DbContext instances for unit tests, composed the way an application composes them:
/// Tenantry registered with <c>AddTenantry</c>, and the context's options calling <c>UseTenantry()</c> with that
/// application service provider.
/// </summary>
/// <remarks>
/// Uses an always-open <see cref="SqliteConnection"/> to keep the in-memory database alive
/// for the lifetime of the test. Pass the connection to all DbContext instances that
/// need to share the same database.
///
/// The test tenant context passed in is registered as the application's <see cref="ITenantContext{TKey}"/>, in
/// place of Tenantry's ambient one. Its current tenant is per async flow, so tests switch tenants with
/// <see cref="TestTenantContext.As"/> and run in parallel without interfering.
/// </remarks>
public static class DbContextFactory
{
    /// <summary>
    /// Creates an open SQLite in-memory connection. Keep this alive for the duration
    /// of the test; disposing it destroys the in-memory database.
    /// </summary>
    public static SqliteConnection CreateSharedConnection()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// The application services a test context runs with: Tenantry for <typeparamref name="TKey"/>, with
    /// <paramref name="tenantContext"/> as the current tenant, and the isolation options given.
    /// </summary>
    public static IServiceProvider Services<TKey>(
        ITenantContext<TKey> tenantContext,
        EfCoreIsolationOptions? isolationOptions = null,
        Action<IServiceCollection>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ServiceCollection services = new();
        services.AddSingleton(tenantContext);
        services.AddTenantry<TKey>(tenant =>
        {
            if (isolationOptions is not null)
            {
                tenant.ConfigureEfCoreIsolation(options =>
                {
                    options.OnMissingTenant = isolationOptions.OnMissingTenant;
                    options.OnSaveWithoutTransaction = isolationOptions.OnSaveWithoutTransaction;
                });
            }
        });
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>
    /// Options for any context type on the given connection, using <c>UseTenantry()</c>.
    /// </summary>
    public static DbContextOptions<TContext> Options<TContext>(
        ITenantContext<string> tenantContext,
        SqliteConnection connection,
        EfCoreIsolationOptions? isolationOptions = null)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(Services(tenantContext, isolationOptions))
            .UseTenantry()
            .Options;

    /// <summary>
    /// Creates a <see cref="TestDbContext"/> on the given connection, with default (<c>Reject</c>) isolation
    /// options unless <paramref name="isolationOptions"/> is supplied. Applies EnsureCreated to set up the schema.
    /// </summary>
    public static async Task<TestDbContext> CreateContextAsync(
        TestTenantContext tenantContext,
        SqliteConnection connection,
        EfCoreIsolationOptions? isolationOptions = null)
    {
        TestDbContext context = new(Options<TestDbContext>(tenantContext, connection, isolationOptions));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// Creates a <see cref="TestDbContext"/> on its own private in-memory database.
    /// Useful for single-tenant tests that don't need to share a database.
    /// </summary>
    public static async Task<(TestDbContext context, SqliteConnection connection)> CreateIsolatedContextAsync(
        TestTenantContext tenantContext)
    {
        var connection = CreateSharedConnection();
        var context = await CreateContextAsync(tenantContext, connection);
        return (context, connection);
    }

    // ── Guid-keyed path ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="GuidTestDbContext"/> on the given connection.
    /// </summary>
    public static async Task<GuidTestDbContext> CreateGuidContextAsync(
        GuidTestTenantContext tenantContext,
        SqliteConnection connection)
    {
        GuidTestDbContext context = new(new DbContextOptionsBuilder<GuidTestDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(Services(tenantContext))
            .UseTenantry()
            .Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// Creates a <see cref="GuidTestDbContext"/> on its own private in-memory database.
    /// </summary>
    public static async Task<(GuidTestDbContext context, SqliteConnection connection)> CreateIsolatedGuidContextAsync(
        GuidTestTenantContext tenantContext)
    {
        var connection = CreateSharedConnection();
        var context = await CreateGuidContextAsync(tenantContext, connection);
        return (context, connection);
    }
}
