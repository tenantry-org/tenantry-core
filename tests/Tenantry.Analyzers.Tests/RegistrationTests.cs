using Microsoft.CodeAnalysis.Testing;
using Tenantry.EfCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>
/// TNY1004: a DbContext with tenant-owned entities registered without UseTenantry(); and the registrations it must leave
/// alone, because they call it or it cannot see that they do not.
/// </summary>
public sealed class RegistrationTests
{
    private const string Model = """
        using System;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Options;
        using Tenantry;
        using Tenantry.EfCore;

        public class Order : TenantEntity<Guid>
        {
            public int Id { get; set; }
        }

        public class Country
        {
            public int Id { get; set; }
        }

        public interface IAppDbContext
        {
        }

        public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Country> Countries => Set<Country>();
        }

        public class ReportsDbContext(DbContextOptions<ReportsDbContext> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        """;

    [Fact]
    public Task EveryRegistrationMethod_WithoutUseTenantry_IsReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public class AppDbContextFactory : IDbContextFactory<AppDbContext>
            {
                public AppDbContext CreateDbContext() => throw new NotSupportedException();
            }

            public class CatalogDbContext(DbContextOptions options) : DbContext(options)
            {
                public DbSet<Country> Countries => Set<Country>();
            }

            public class StoreDbContext(DbContextOptions<StoreDbContext> options) : CatalogDbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<IAppDbContext, AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<IAppDbContext, AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextPool<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextPool<AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextPool<IAppDbContext, AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextPool<IAppDbContext, AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextFactory<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextFactory<AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextFactory<AppDbContext, AppDbContextFactory>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextFactory<AppDbContext, AppDbContextFactory>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddPooledDbContextFactory<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddPooledDbContextFactory<AppDbContext>((_, options) => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<CatalogDbContext, StoreDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<AppDbContext>(options =>
                    {
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                    })|};
                }
            }
            """);

    [Fact]
    public Task TheMessage_NamesTheContext() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(
            Model + """
                public static class Startup
                {
                    public static void Register(IServiceCollection services) =>
                        {|#0:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                }
                """,
            new DiagnosticResult(Rules.ContextWithoutUseTenantry).WithLocation(0).WithArguments("AppDbContext"));

    [Fact]
    public Task ARegistrationThatCallsUseTenantry_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging().UseTenantry());
                    services.AddDbContextPool<ReportsDbContext>(options =>
                    {
                        options.EnableSensitiveDataLogging();
                        options.UseTenantry();
                    });
                    services.AddDbContextFactory<AuditDbContext>((_, options) =>
                        options.UseTenantry(isolation => isolation.OnMissingTenant = MissingTenantBehavior.Allow));
                }
            }
            """);

    [Fact]
    public Task UseTenantryInANestedLambdaOrALocalFunction_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services, bool isolate)
                {
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        void Isolate() => options.UseTenantry();
                        Isolate();
                    });
                    services.AddDbContext<ReportsDbContext>(options => When(isolate, () => options.UseTenantry()));
                }

                private static void When(bool condition, Action action)
                {
                    if (condition)
                        action();
                }
            }
            """);

    [Fact]
    public Task ABuilderHandedToOtherCode_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public interface IContextConfigurer
            {
                void Configure(DbContextOptionsBuilder options);
            }

            public class Holder
            {
                public DbContextOptionsBuilder? Options { get; set; }
            }

            public class Configurer(DbContextOptionsBuilder options)
            {
                public DbContextOptionsBuilder Options { get; } = options;
            }

            public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public class JournalDbContext(DbContextOptions<JournalDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public static class Startup
            {
                public static void Register(
                    IServiceCollection services,
                    Action<DbContextOptionsBuilder> shared,
                    IContextConfigurer configurer,
                    IConfigureOptions<DbContextOptionsBuilder> configureOptions,
                    Holder holder)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging().UseOurDefaults());
                    services.AddDbContext<ReportsDbContext>(options => shared(options));
                    services.AddDbContext<BillingDbContext>(options => configurer.Configure(options));
                    services.AddDbContext<AuditDbContext>(options => holder.Options = options);
                    services.AddDbContext<LedgerDbContext>(options => Remember(options));
                    services.AddDbContext<ArchiveDbContext>(options => new Configurer(options));
                    services.AddDbContext<JournalDbContext>(options => configureOptions.Configure(options));
                }

                private static DbContextOptionsBuilder UseOurDefaults(this DbContextOptionsBuilder options) =>
                    options.EnableDetailedErrors();

                private static void Remember(object options)
                {
                }
            }
            """);

    [Fact]
    public Task ABuilderHandedToALibraryThatReferencesTenantryEfCore_IsNotReported_AndToOneThatDoesNotIs() =>
        Verify.AnalyzerWithLibraryAsync<ContextWithoutUseTenantryAnalyzer>(
            Model + """
                public static class Startup
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddDbContext<AppDbContext>(options => options.UseOurIsolation());
                        {|TNY1004:services.AddDbContext<ReportsDbContext>(options => Console.WriteLine(options))|};
                    }
                }
                """,
            """
            using Microsoft.EntityFrameworkCore;

            public static class OurIsolation
            {
                public static DbContextOptionsBuilder UseOurIsolation(this DbContextOptionsBuilder options) => options.UseTenantry();
            }
            """);

    [Fact]
    public Task OptionsGivenAsAMethod_AreReportedOnlyWhenTheMethodDoesNotCallUseTenantry() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<AppDbContext>(Logged)|};
                    services.AddDbContext<ReportsDbContext>(Isolated);
                    services.AddDbContext<BillingDbContext>(Local);

                    void Local(DbContextOptionsBuilder options) => options.UseTenantry();
                }

                private static void Logged(DbContextOptionsBuilder options) => options.EnableSensitiveDataLogging();

                private static void Isolated(DbContextOptionsBuilder options) => options.UseTenantry();
            }
            """);

    [Fact]
    public Task RegistrationsWithoutOptionsOrWithADelegateItCannotSee_AreNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services, Action<DbContextOptionsBuilder> configure)
                {
                    services.AddDbContext<AppDbContext>();
                    services.AddDbContext<ReportsDbContext>(ServiceLifetime.Transient);
                    services.AddDbContext<BillingDbContext>(configure);
                }
            }
            """);

    [Fact]
    public Task ARegistrationWithoutOptions_ClearsNoOtherRegistration() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>();
                    services.AddDbContext<ReportsDbContext>(ServiceLifetime.Transient);
                    {|TNY1004:services.AddDbContextFactory<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContextFactory<ReportsDbContext>(options => options.EnableSensitiveDataLogging())|};
                }
            }
            """);

    [Fact]
    public Task AContextThatOverridesOnConfiguring_OrDerivesFromOneThatDoes_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public class ConfiguredDbContext(DbContextOptions options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();

                protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseTenantry();
            }

            public class DerivedDbContext(DbContextOptions<DerivedDbContext> options) : ConfiguredDbContext(options);

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<ConfiguredDbContext>(options => options.EnableSensitiveDataLogging());
                    services.AddDbContext<DerivedDbContext>(options => options.EnableSensitiveDataLogging());
                }
            }
            """);

    [Fact]
    public Task AnotherRegistrationOfTheSameContextWithUseTenantry_ClearsIt() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging());
                    services.AddDbContextFactory<AppDbContext>(options => options.UseTenantry());
                    {|TNY1004:services.AddDbContext<ReportsDbContext>(options => options.EnableSensitiveDataLogging())|};
                }
            }
            """);

#if NET9_0_OR_GREATER
    [Fact]
    public Task ConfigureDbContextWithUseTenantry_ClearsIt_AndWithoutDoesNot() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging());
                    services.ConfigureDbContext<AppDbContext>(options => options.UseTenantry());

                    {|TNY1004:services.AddDbContext<ReportsDbContext>(options => options.EnableSensitiveDataLogging())|};
                    services.ConfigureDbContext<ReportsDbContext>(options => options.EnableDetailedErrors());
                }
            }
            """);
#endif

    [Fact]
    public Task AGenericRegistration_IsNotReported_AndOneThatCallsUseTenantryClearsEveryContext() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging());
                    services.AddUnisolated<AppDbContext>();
                    services.AddIsolation<AppDbContext>();
                }

                private static void AddUnisolated<TContext>(this IServiceCollection services) where TContext : DbContext =>
                    services.AddDbContext<TContext>(options => options.EnableDetailedErrors());

                private static void AddIsolation<TContext>(this IServiceCollection services) where TContext : DbContext =>
                    services.AddDbContextFactory<TContext>(options => options.UseTenantry());
            }
            """);

    [Fact]
    public Task AGenericRegistrationWithoutUseTenantry_ClearsNothing() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    services.AddUnisolated<AppDbContext>();
                }

                private static void AddUnisolated<TContext>(this IServiceCollection services) where TContext : DbContext =>
                    services.AddDbContextFactory<TContext>(options => options.EnableDetailedErrors());
            }
            """);

    [Fact]
    public Task ATenantOwnedSetOnABaseContext_OrOfADerivedEntity_IsReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public abstract class Owned : ITenantEntity<Guid>
            {
                public Guid TenantId { get; set; }
            }

            public class Payment : Owned
            {
                public int Id { get; set; }
            }

            public abstract class BaseDbContext(DbContextOptions options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : BaseDbContext(options);

            public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
            {
                public DbSet<Payment> Payments => Set<Payment>();
            }

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<ArchiveDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<PaymentsDbContext>(options => options.EnableSensitiveDataLogging())|};
                }
            }
            """);

    [Fact]
    public Task AContextWithOnlySharedEntities_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
            {
                public DbSet<Country> Countries => Set<Country>();
            }

            public static class Startup
            {
                public static void Register(IServiceCollection services) =>
                    services.AddDbContext<CatalogDbContext>(options => options.EnableSensitiveDataLogging());
            }
            """);

    [Fact]
    public Task AddDbContextPerTenantDatabase_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                // It applies UseTenantry() itself.
                public static void Register(IServiceCollection services) =>
                    services.AddTenantry<Guid>(tenant => tenant
                        .UseInMemoryStore([])
                        .AddDbContextPerTenantDatabase<AppDbContext>((_, options) => options.EnableSensitiveDataLogging()));
            }
            """);
}
