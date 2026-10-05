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
    public Task UseTenantryOnTheGenericBuilder_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        var typed = (DbContextOptionsBuilder<AppDbContext>)options;
                        typed.EnableSensitiveDataLogging().UseTenantry();
                    });
                    services.AddDbContext<ReportsDbContext>(Typed);
                }

                private static void Typed(DbContextOptionsBuilder options)
                {
                    if (options is DbContextOptionsBuilder<ReportsDbContext> typed)
                        typed.UseTenantry();
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
    public Task ABuilderHandedToCodeThatMayCallUseTenantry_IsNotReported() =>
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

            public static class Startup
            {
                public static void Register(
                    IServiceCollection services,
                    Action<DbContextOptionsBuilder> shared,
                    IContextConfigurer configurer,
                    IConfigureOptions<DbContextOptionsBuilder> configureOptions,
                    Holder holder)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging().UseOurIsolation());
                    services.AddDbContext<ReportsDbContext>(options => shared(options));
                    services.AddDbContext<BillingDbContext>(options => configurer.Configure(options));
                    services.AddDbContext<AuditDbContext>(options => holder.Options = options);
                    services.AddDbContext<LedgerDbContext>(options => ForwardAgain(options));
                    services.AddDbContext<ArchiveDbContext>(options => new Configurer(options));
                    services.AddDbContext<JournalDbContext>(options => configureOptions.Configure(options));
                }

                private static DbContextOptionsBuilder UseOurIsolation(this DbContextOptionsBuilder options) =>
                    options.EnableDetailedErrors().UseTenantry();

                // Through other methods of the application's, in turn.
                private static void ForwardAgain(DbContextOptionsBuilder options) => Forward(options);

                private static void Forward(object options) => ((DbContextOptionsBuilder)options).UseOurIsolation();
            }
            """);

    [Fact]
    public Task ABuilderHandedToALocalFunction_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => Configure(options));

                    static void Configure(DbContextOptionsBuilder options) => options.EnableSensitiveDataLogging();
                }
            }
            """);

    [Fact]
    public Task ABuilderHandedToAMethodOfTheApplicationsThatDoesNotCallUseTenantry_IsReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging().UseLogging())|};
                    {|TNY1004:services.AddDbContext<ReportsDbContext>(options => Remember(options))|};
                    {|TNY1004:services.AddDbContext<BillingDbContext>(options => Recurse(options, 3))|};
                }

                private static DbContextOptionsBuilder UseLogging(this DbContextOptionsBuilder options) =>
                    options.EnableDetailedErrors();

                private static void Remember(object options)
                {
                }

                private static void Recurse(DbContextOptionsBuilder options, int depth)
                {
                    if (depth > 0)
                        Recurse(options.EnableDetailedErrors(), depth - 1);
                }
            }
            """);

    [Fact]
    public Task ABuilderHandedToALibrary_IsNotReported_OnlyWhereTheLibraryCanReachTenantryEfCore() =>
        Verify.AnalyzerWithLibrariesAsync<ContextWithoutUseTenantryAnalyzer>(
            Model + """
                public static class Startup
                {
                    public static void Register(IServiceCollection services, Unsealed unsealed, Sealed @sealed)
                    {
                        services.AddDbContext<AppDbContext>(options => options.UseIso());
                        services.AddDbContext<ReportsDbContext>(options => options.UseCompanyDefaults());
                        services.AddDbContext<BillingDbContext>(options => unsealed.Apply(options));
                        {|TNY1004:services.AddDbContext<AuditDbContext>(options => @sealed.Apply(options))|};
                        {|TNY1004:services.AddDbContext<LedgerDbContext>(options => options.UseLogging())|};
                        {|TNY1004:services.AddDbContext<ArchiveDbContext>(options => Console.WriteLine(options))|};
                    }
                }
                """,
            [
                new("Iso", """
                    using Microsoft.EntityFrameworkCore;

                    public static class Iso
                    {
                        public static DbContextOptionsBuilder UseIso(this DbContextOptionsBuilder options) => options.UseTenantry();
                    }
                    """),
                // Calls Iso, without a reference to Tenantry.EfCore of its own.
                new("Wrapper", """
                    using Microsoft.EntityFrameworkCore;

                    public static class Company
                    {
                        public static DbContextOptionsBuilder UseCompanyDefaults(this DbContextOptionsBuilder options) =>
                            options.EnableDetailedErrors().UseIso();
                    }
                    """, ReferencesTenantry: false) { Uses = ["Iso"] },
                // Reaches no Tenantry.EfCore: only an override the application can override in turn may call it.
                new("Logging", """
                    using Microsoft.EntityFrameworkCore;

                    public static class Logging
                    {
                        public static DbContextOptionsBuilder UseLogging(this DbContextOptionsBuilder options) =>
                            options.EnableSensitiveDataLogging();
                    }

                    public abstract class Base
                    {
                        public abstract void Apply(DbContextOptionsBuilder options);
                    }

                    public class Unsealed : Base
                    {
                        public override void Apply(DbContextOptionsBuilder options)
                        {
                        }
                    }

                    public class Sealed : Base
                    {
                        public sealed override void Apply(DbContextOptionsBuilder options)
                        {
                        }
                    }
                    """, ReferencesTenantry: false),
            ]);

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
                    {|TNY1004:services.AddDbContext<AuditDbContext>(LocalLogged)|};
                    services.AddDbContext<LedgerDbContext>(IsolatedInALocalFunction);

                    void Local(DbContextOptionsBuilder options) => options.UseTenantry();

                    // Judged by itself, though the method it is in calls UseTenantry().
                    void LocalLogged(DbContextOptionsBuilder options) => options.EnableSensitiveDataLogging();
                }

                private static void Logged(DbContextOptionsBuilder options) => options.EnableSensitiveDataLogging();

                private static void Isolated(DbContextOptionsBuilder options) => options.UseTenantry();

                private static void IsolatedInALocalFunction(DbContextOptionsBuilder options)
                {
                    Isolate();

                    void Isolate() => options.UseTenantry();
                }
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
    public Task OnConfiguringThatMayCallUseTenantry_OrInAnotherAssembly_ClearsIt_AndOtherwiseDoesNot() =>
        Verify.AnalyzerWithLibrariesAsync<ContextWithoutUseTenantryAnalyzer>(
            Model + """
                public class IsolatingDbContext(DbContextOptions options) : DbContext(options)
                {
                    public DbSet<Order> Orders => Set<Order>();

                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseTenantry();
                }

                public class DerivedDbContext(DbContextOptions<DerivedDbContext> options) : IsolatingDbContext(options);

                public class ChainedDbContext(DbContextOptions<ChainedDbContext> options) : IsolatingDbContext(options)
                {
                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                    {
                        base.OnConfiguring(optionsBuilder);
                        optionsBuilder.EnableDetailedErrors();
                    }
                }

                // A scaffolded context: OnConfiguring picks the provider.
                public class ScaffoldedDbContext(DbContextOptions<ScaffoldedDbContext> options) : DbContext(options)
                {
                    public DbSet<Order> Orders => Set<Order>();

                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                    {
                        base.OnConfiguring(optionsBuilder);

                        if (!optionsBuilder.IsConfigured)
                            optionsBuilder.EnableSensitiveDataLogging();
                    }
                }

                public class LoggingBaseDbContext(DbContextOptions options) : DbContext(options)
                {
                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
                        optionsBuilder.EnableDetailedErrors();
                }

                public class LoggedDbContext(DbContextOptions<LoggedDbContext> options) : LoggingBaseDbContext(options)
                {
                    public DbSet<Order> Orders => Set<Order>();

                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => base.OnConfiguring(optionsBuilder);
                }

                public class LibraryBasedDbContext(DbContextOptions<LibraryBasedDbContext> options) : LibraryDbContext(options)
                {
                    public DbSet<Order> Orders => Set<Order>();
                }

                public static class Startup
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddDbContext<IsolatingDbContext>(options => options.EnableSensitiveDataLogging());
                        services.AddDbContext<DerivedDbContext>(options => options.EnableSensitiveDataLogging());
                        services.AddDbContext<ChainedDbContext>(options => options.EnableSensitiveDataLogging());
                        services.AddDbContext<LibraryBasedDbContext>(options => options.EnableSensitiveDataLogging());
                        {|TNY1004:services.AddDbContext<ScaffoldedDbContext>(options => options.EnableSensitiveDataLogging())|};
                        {|TNY1004:services.AddDbContext<LoggedDbContext>(options => options.EnableSensitiveDataLogging())|};
                    }
                }
                """,
            [
                new("Contexts", """
                    using Microsoft.EntityFrameworkCore;

                    public class LibraryDbContext(DbContextOptions options) : DbContext(options)
                    {
                        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                        {
                        }
                    }
                    """, ReferencesTenantry: false),
            ]);

#if NET9_0_OR_GREATER
    [Fact]
    public Task AnotherRegistrationOfTheSameContextWithUseTenantry_ClearsIt_FromEfCore9() =>
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
#else
    [Fact]
    public Task OnEfCore8_OnlyAnEarlierRegistrationWithUseTenantry_OrOneElsewhere_ClearsIt() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    // The first registration's options are the context's.
                    {|TNY1004:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    services.AddDbContextFactory<AppDbContext>(options => options.UseTenantry());

                    services.AddDbContextFactory<ReportsDbContext>(options => options.UseTenantry());
                    services.AddDbContext<ReportsDbContext>(options => options.EnableSensitiveDataLogging());

                    services.AddDbContext<BillingDbContext>(options => options.EnableSensitiveDataLogging());
                    AddIsolatedBilling(services);
                }

                // Its order against the registration above is not known here.
                private static void AddIsolatedBilling(IServiceCollection services) =>
                    services.AddDbContextFactory<BillingDbContext>(options => options.UseTenantry());
            }
            """);
#endif

    [Fact]
    public Task AGenericRegistrationWithUseTenantry_ClearsTheContextsThatMeetItsConstraints() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public interface IIsolated
            {
            }

            public class IsolatedDbContext(DbContextOptions<IsolatedDbContext> options) : DbContext(options), IIsolated
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<IsolatedDbContext>(options => options.EnableSensitiveDataLogging());
                    {|TNY1004:services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging())|};
                    services.AddIsolation<IsolatedDbContext>();
                }

                private static void AddIsolation<TContext>(this IServiceCollection services) where TContext : DbContext, IIsolated =>
                    services.AddDbContextFactory<TContext>(options => options.UseTenantry());
            }
            """);

    [Fact]
    public Task AGenericRegistrationWithUseTenantry_ConstrainedOnlyToDbContext_ClearsEveryContext() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddDbContext<AppDbContext>(options => options.EnableSensitiveDataLogging());
                    services.AddDbContext<ReportsDbContext>(options => options.EnableSensitiveDataLogging());
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
    public Task AGenericFactoryRegistration_IsForItsContextType_NotItsFactorysTypeParameter() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public class AppDbContextFactory : IDbContextFactory<AppDbContext>
            {
                public AppDbContext CreateDbContext() => throw new NotSupportedException();
            }

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<ReportsDbContext>(options => options.EnableSensitiveDataLogging())|};
                    services.AddFactory<AppDbContextFactory>();
                }

                private static void AddFactory<TFactory>(this IServiceCollection services) where TFactory : IDbContextFactory<AppDbContext> =>
                    services.AddDbContextFactory<AppDbContext, TFactory>(options => options.UseTenantry());
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
    public Task ATenantOwnedTypeOnABaseContext_OfADerivedEntity_OrMappedInOnModelCreating_IsReported() =>
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

            public class StoreDbContext(DbContextOptions<StoreDbContext> options) : BaseDbContext(options);

            public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
            {
                public DbSet<Payment> Payments => Set<Payment>();
            }

            public class MappedDbContext(DbContextOptions<MappedDbContext> options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>();
            }

            public abstract class MappingBaseDbContext(DbContextOptions options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Payment>();
            }

            public class InheritedDbContext(DbContextOptions<InheritedDbContext> options) : MappingBaseDbContext(options);

            public abstract class GenericDbContext<TEntity>(DbContextOptions options) : DbContext(options)
                where TEntity : class, ITenantEntity<Guid>
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<TEntity>();
            }

            public class PaymentLedgerDbContext(DbContextOptions<PaymentLedgerDbContext> options) : GenericDbContext<Payment>(options);

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                {
                    {|TNY1004:services.AddDbContext<StoreDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<PaymentsDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<MappedDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<InheritedDbContext>(options => options.EnableSensitiveDataLogging())|};
                    {|TNY1004:services.AddDbContext<PaymentLedgerDbContext>(options => options.EnableSensitiveDataLogging())|};
                }
            }
            """);

    [Fact]
    public Task AContextWithOnlySharedEntities_IsNotReported() =>
        Verify.AnalyzerAsync<ContextWithoutUseTenantryAnalyzer>(Model + """
            public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
            {
                public DbSet<Country> Countries => Set<Country>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Country>();
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
