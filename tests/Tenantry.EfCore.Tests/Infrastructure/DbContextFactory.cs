using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Infrastructure;

/// <summary>
/// Creates in-memory SQLite DbContext instances for unit tests.
/// </summary>
/// <remarks>
/// Uses an always-open <see cref="SqliteConnection"/> to keep the in-memory database alive
/// for the lifetime of the test. Pass the connection to all DbContext instances that
/// need to share the same database.
///
/// EF Core caches the model per DbContext type. The query filter captures the
/// <see cref="TestTenantContext"/> passed during model construction. All DbContext
/// instances sharing a model will evaluate the filter against that same captured instance.
/// Tests must use a SINGLE mutable <see cref="TestTenantContext"/> and switch
/// <see cref="TestTenantContext.CurrentTenantId"/> between operations.
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
    /// Creates a <see cref="TestDbContext"/> (interceptor path) on the given connection.
    /// Applies EnsureCreated to set up the schema.
    /// </summary>
    public static async Task<TestDbContext> CreateInterceptorContextAsync(
        TestTenantContext tenantContext,
        SqliteConnection connection)
    {
        var options = BuildInterceptorOptions(tenantContext, connection);
        TestDbContext context = new(options, tenantContext);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// Creates a <see cref="TestDbContext"/> on its own private in-memory database.
    /// Useful for single-tenant tests that don't need to share a database.
    /// </summary>
    public static async Task<(TestDbContext context, SqliteConnection connection)> CreateIsolatedInterceptorContextAsync(
        TestTenantContext tenantContext)
    {
        var connection = CreateSharedConnection();
        var context = await CreateInterceptorContextAsync(tenantContext, connection);
        return (context, connection);
    }

    /// <summary>
    /// Creates a <see cref="BaseClassTestDbContext"/> (base class path) on the given connection.
    /// </summary>
    public static async Task<BaseClassTestDbContext> CreateBaseClassContextAsync(
        TestTenantContext tenantContext,
        SqliteConnection connection)
    {
        var options = BuildBaseClassInterceptorOptions(tenantContext, connection);

        BaseClassTestDbContext context = new(options, tenantContext);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// Creates a <see cref="BaseClassTestDbContext"/> on its own private in-memory database.
    /// </summary>
    public static async Task<(BaseClassTestDbContext context, SqliteConnection connection)> CreateIsolatedBaseClassContextAsync(
        TestTenantContext tenantContext)
    {
        var connection = CreateSharedConnection();
        var context = await CreateBaseClassContextAsync(tenantContext, connection);
        return (context, connection);
    }

    // ── Guid-keyed path ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="GuidTestDbContext"/> (interceptor path) on the given connection.
    /// </summary>
    public static async Task<GuidTestDbContext> CreateGuidInterceptorContextAsync(
        GuidTestTenantContext tenantContext,
        SqliteConnection connection)
    {
        DbContextOptions<GuidTestDbContext> options = BuildGuidInterceptorOptions(tenantContext, connection);
        GuidTestDbContext context = new(options, tenantContext);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// Creates a <see cref="GuidTestDbContext"/> on its own private in-memory database.
    /// </summary>
    public static async Task<(GuidTestDbContext context, SqliteConnection connection)> CreateIsolatedGuidInterceptorContextAsync(
        GuidTestTenantContext tenantContext)
    {
        SqliteConnection connection = CreateSharedConnection();
        GuidTestDbContext context = await CreateGuidInterceptorContextAsync(tenantContext, connection);
        return (context, connection);
    }

    private static DbContextOptions<TestDbContext> BuildInterceptorOptions(
        TestTenantContext tenantContext,
        SqliteConnection connection)
    {
        TenantSaveChangesInterceptor<string> interceptor = new(
            tenantContext,
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance,
            [],
            []);

        return new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
    }

    private static DbContextOptions<BaseClassTestDbContext> BuildBaseClassInterceptorOptions(
        TestTenantContext tenantContext,
        SqliteConnection connection)
    {
        TenantSaveChangesInterceptor<string> interceptor = new(
            tenantContext,
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance,
            [],
            []);

        return new DbContextOptionsBuilder<BaseClassTestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
    }

    private static DbContextOptions<GuidTestDbContext> BuildGuidInterceptorOptions(
        GuidTestTenantContext tenantContext,
        SqliteConnection connection)
    {
        TenantSaveChangesInterceptor<Guid> interceptor = new(
            tenantContext,
            NullLogger<TenantSaveChangesInterceptor<Guid>>.Instance,
            [],
            []);

        return new DbContextOptionsBuilder<GuidTestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
    }
}
