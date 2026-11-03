using Microsoft.CodeAnalysis.Testing;
using Tenantry.EfCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>
/// TNY1001: an entity with a TenantId that does not implement ITenantEntity&lt;TKey&gt;, in a context that maps a
/// tenant-owned type; and the shapes of correct models it must leave alone.
/// </summary>
public sealed class TenantEntityTests
{
    private const string Usings = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Tenantry;
        using Tenantry.EfCore;

        public class Invoice : TenantEntity<Guid>
        {
            public int Id { get; set; }
        }

        """;

    [Fact]
    public Task ADbSetOfAnEntityWithATenantIdThatIsNotTenantOwned_IsReported() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }
            """);

    [Fact]
    public Task AnEntityMappedInOnModelCreating_IsReported_AndSoIsAnInheritedTenantId() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public abstract class Owned
            {
                public string TenantId { get; set; } = "";
            }

            public class Payment : Owned
            {
                public int Id { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => {|TNY1001:modelBuilder.Entity<Payment>()|};
            }
            """);

    [Fact]
    public Task ATypeMappedTwice_IsReportedOnce_WhereItIsFirstMapped() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                    modelBuilder.Entity<Order>().HasIndex(order => order.TenantId);
            }

            public class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();
            }
            """);

    [Fact]
    public Task TypesMarkedSharedFluently_AreNotReported() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Import
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Feed
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Rate
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class RateConfiguration : IEntityTypeConfiguration<Rate>
            {
                public void Configure(EntityTypeBuilder<Rate> builder) => builder.IsSharedAcrossTenants();
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Import> Imports => Set<Import>();
                public DbSet<Feed> Feeds => Set<Feed>();
                public DbSet<Rate> Rates => Set<Rate>();

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Import>().IsSharedAcrossTenants();
                    modelBuilder.Entity(typeof(Feed)).IsSharedAcrossTenants();
                    modelBuilder.ApplyConfiguration(new RateConfiguration());
                }
            }
            """);

    [Fact]
    public Task TenantOwnedTypesAttributesDescriptorsAndRegistries_AreNotReported() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Line : ITenantEntity<Guid>
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            [SharedAcrossTenants]
            public class Template
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class SystemTemplate : Template
            {
            }

            // Registries of tenants, keyed by their TenantId.
            public class Tenant
            {
                public Guid TenantId { get; set; }
                public string Name { get; set; } = "";
            }

            public class TenantRecord
            {
                public Guid TenantId { get; set; }
                public string Name { get; set; } = "";
            }

            public class Organisation
            {
                [Key]
                public Guid TenantId { get; set; }
                public int Id { get; set; }
            }

            [PrimaryKey(nameof(TenantId))]
            public class Subscription
            {
                public Guid TenantId { get; set; }
                public int Id { get; set; }
            }

            public class Customer : TenantDescriptor<Guid>
            {
                public string Plan { get; set; } = "";
            }

            public class Country
            {
                public int Id { get; set; }
                public string Code { get; set; } = "";
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Line> Lines => Set<Line>();
                public DbSet<Template> Templates => Set<Template>();
                public DbSet<SystemTemplate> SystemTemplates => Set<SystemTemplate>();
                public DbSet<Tenant> Tenants => Set<Tenant>();
                public DbSet<TenantRecord> TenantRecords => Set<TenantRecord>();
                public DbSet<Organisation> Organisations => Set<Organisation>();
                public DbSet<Subscription> Subscriptions => Set<Subscription>();
                public DbSet<Customer> Customers => Set<Customer>();
                public DbSet<Country> Countries => Set<Country>();
            }
            """);

    [Fact]
    public Task AContextWithNoTenantOwnedType_IsLeftAlone() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            // A database per tenant, or a context Tenantry does not isolate: its rows carry a TenantId of their own.
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public class Holder
            {
                public DbSet<Order>? Orders { get; set; }
            }
            """);

    [Fact]
    public Task AContextMapsItsBaseContextsTypes() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public abstract class BaseDbContext(DbContextOptions options) : DbContext(options)
            {
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : BaseDbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
            }
            """);

    [Fact]
    public Task AGenericBaseContext_MapsTheTypeArgumentsTheDerivedContextPasses() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class AppUser
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppRole
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public abstract class UsersDbContext<TUser, TRole>(DbContextOptions options) : DbContext(options)
                where TUser : class
                where TRole : class
            {
                public DbSet<TUser> {|TNY1001:Users|} => Set<TUser>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => {|TNY1001:modelBuilder.Entity<TRole>()|};
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : UsersDbContext<AppUser, AppRole>(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
            }
            """);

    [Fact]
    public Task ABaseContextFromAnotherAssembly_MapsItsDbSets_ReportedOnTheDerivedContext() =>
        Verify.AnalyzerWithLibrariesAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                public class AppUser
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public class Member
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                // As IdentityDbContext<TUser> maps the user type through its own DbSet<TUser> Users.
                public class {|TNY1001:AppIdentityDbContext|}(DbContextOptions<AppIdentityDbContext> options)
                    : PackagedUsersDbContext<AppUser>(options)
                {
                    public DbSet<Invoice> Invoices => Set<Invoice>();
                }

                public class {|TNY1001:MembersDbContext|}(DbContextOptions<MembersDbContext> options)
                    : ProjectUsersDbContext<Member>(options)
                {
                    public DbSet<Invoice> Invoices => Set<Invoice>();
                }
                """,
            [
                new("Packaged", """
                    using Microsoft.EntityFrameworkCore;

                    public class PackagedUsersDbContext<TUser>(DbContextOptions options) : DbContext(options) where TUser : class
                    {
                        public DbSet<TUser> Users => Set<TUser>();
                    }
                    """, ReferencesTenantry: false, AsReferenceAssembly: true),
                new("Project", """
                    using Microsoft.EntityFrameworkCore;

                    public class ProjectUsersDbContext<TUser>(DbContextOptions options) : DbContext(options) where TUser : class
                    {
                        public DbSet<TUser> Users => Set<TUser>();
                    }
                    """, ReferencesTenantry: false),
            ]);

    [Fact]
    public Task ABaseContextFromAnotherAssembly_ItsOwnTypesAreLeftToItsProject() =>
        Verify.AnalyzerWithLibrariesAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                // Country is marked shared in the other project, by a marker this compilation cannot see.
                public class TestAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options);
                """,
            [
                new("App", """
                    using System;
                    using Microsoft.EntityFrameworkCore;
                    using Tenantry;
                    using Tenantry.EfCore;

                    public class Payment : TenantEntity<Guid>
                    {
                        public int Id { get; set; }
                    }

                    public class Country
                    {
                        public int Id { get; set; }
                        public Guid TenantId { get; set; }
                    }

                    public class AppDbContext(DbContextOptions options) : DbContext(options)
                    {
                        public DbSet<Payment> Payments => Set<Payment>();
                        public DbSet<Country> Countries => Set<Country>();

                        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                            modelBuilder.Entity<Country>().IsSharedAcrossTenants();
                    }
                    """, AsReferenceAssembly: true),
            ]);

    [Fact]
    public Task ABaseContextFromAnotherAssembly_ReportsOnlyTheTypesThisContextPasses_NotThoseItsBasePasses() =>
        Verify.AnalyzerWithLibrariesAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                public class Member
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public class {|TNY1001:MembersDbContext|}(DbContextOptions<MembersDbContext> options)
                    : RegionalUsersDbContext<Member>(options)
                {
                    public DbSet<Invoice> Invoices => Set<Invoice>();
                }
                """,
            [
                new("Users", """
                    using System;
                    using Microsoft.EntityFrameworkCore;

                    public class Region
                    {
                        public int Id { get; set; }
                        public Guid TenantId { get; set; }
                    }

                    public class UsersDbContext<TUser, TRegion>(DbContextOptions options) : DbContext(options)
                        where TUser : class
                        where TRegion : class
                    {
                        public DbSet<TUser> Users => Set<TUser>();
                        public DbSet<TRegion> Regions => Set<TRegion>();
                    }

                    // Region is marked shared here, by a marker the derived context's compilation cannot see.
                    public class RegionalUsersDbContext<TUser>(DbContextOptions options) : UsersDbContext<TUser, Region>(options)
                        where TUser : class
                    {
                        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                            modelBuilder.Entity<Region>().IsSharedAcrossTenants();
                    }
                    """, AsReferenceAssembly: true),
            ]);

    [Fact]
    public Task ABaseContextFromAnotherAssembly_IsReportedOnTheContextsHandWrittenDeclaration_NotInGeneratedCode() =>
        Verify.AnalyzerWithFilesAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                public class AppUser
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public class Guest
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public partial class {|TNY1001:AppIdentityDbContext|}(DbContextOptions<AppIdentityDbContext> options)
                    : PackagedUsersDbContext<AppUser>(options);
                """,
            [
                // Before the test file in source order, where a type is reported.
                ("/0/Contexts.g.cs", """
                    using Microsoft.EntityFrameworkCore;

                    public partial class AppIdentityDbContext
                    {
                        public DbSet<Invoice> Invoices => Set<Invoice>();
                    }

                    public class GuestsDbContext(DbContextOptions<GuestsDbContext> options) : PackagedUsersDbContext<Guest>(options)
                    {
                        public DbSet<Invoice> Invoices => Set<Invoice>();
                    }
                    """),
            ],
            [
                new("Packaged", """
                    using Microsoft.EntityFrameworkCore;

                    public class PackagedUsersDbContext<TUser>(DbContextOptions options) : DbContext(options) where TUser : class
                    {
                        public DbSet<TUser> Users => Set<TUser>();
                    }
                    """, ReferencesTenantry: false, AsReferenceAssembly: true),
            ]);

    [Fact]
    public Task AnUnknownMarkerInAGenericBaseContext_SilencesTheContextsDerivedFromIt() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public abstract class SharingDbContext<T>(DbContextOptions options) : DbContext(options) where T : class
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                        modelBuilder.Entity(entityType.ClrType).IsSharedAcrossTenants();
                }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : SharingDbContext<Invoice>(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();
            }
            """);

    [Fact]
    public Task AMarkerOnABuilderOfAnUnknownType_MakesItReportNothing() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                        modelBuilder.Entity(entityType.ClrType).IsSharedAcrossTenants();
                }
            }
            """);

    [Fact]
    public Task AMarkerInAGenericHelper_MarksTheTypeItIsCalledWith() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Country
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Country> Countries => Set<Country>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => Shared<Country>(modelBuilder);

                private static void Shared<T>(ModelBuilder b) where T : class => b.Entity<T>().IsSharedAcrossTenants();
            }
            """);

    [Fact]
    public Task AGenericHelperThatPassesItsTypeParameterOn_MarksTheTypeTheOuterCallPasses() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Refund
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public static class Sharing
            {
                public static void Mark<T>(ModelBuilder b) where T : class => b.Entity<T>().IsSharedAcrossTenants();

                public static void Forward<T>(ModelBuilder b) where T : class => Mark<T>(b);
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();
                public DbSet<Refund> {|TNY1001:Refunds|} => Set<Refund>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => Sharing.Forward<Order>(modelBuilder);
            }
            """);

    [Fact]
    public Task AGenericHelperNothingCalls_SilencesNothing() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();

                private static void Shared<T>(ModelBuilder b) where T : class => b.Entity<T>().IsSharedAcrossTenants();

                private static void SharedNested<T>(ModelBuilder b) where T : class => Shared<T>(b);
            }
            """);

    [Fact]
    public Task AnUnknownMarker_SilencesOnlyTheContextsThatApplyIt() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Rate
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class UntypedRateConfiguration : IEntityTypeConfiguration<Rate>
            {
                public void Configure(EntityTypeBuilder<Rate> builder)
                {
                    EntityTypeBuilder untyped = builder;
                    untyped.IsSharedAcrossTenants();
                }
            }

            // Marks in its own OnModelCreating, so it and the context derived from it report nothing.
            public class ImportDbContext(DbContextOptions options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                        modelBuilder.Entity(entityType.ClrType).IsSharedAcrossTenants();
                }
            }

            public class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : ImportDbContext(options)
            {
                public DbSet<Rate> Rates => Set<Rate>();
            }

            // Applies the configuration that marks, so it reports nothing.
            public class RatesDbContext(DbContextOptions<RatesDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Rate> Rates => Set<Rate>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                    modelBuilder.ApplyConfiguration(new UntypedRateConfiguration());
            }

            // Applies neither, so it still reports.
            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }
            """);

    [Fact]
    public Task AnUnknownMarkerInAHelper_SilencesOnlyTheContextThatCallsIt() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public static class Sharing
            {
                public static void MarkAll(ModelBuilder b)
                {
                    foreach (var entityType in b.Model.GetEntityTypes())
                        b.Entity(entityType.ClrType).IsSharedAcrossTenants();
                }
            }

            public class ImportDbContext(DbContextOptions<ImportDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> Orders => Set<Order>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => Sharing.MarkAll(modelBuilder);
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }
            """);

    [Fact]
    public Task AnUnknownMarkerNoContextApplies_SilencesNothing() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            // Dead code: nothing calls it.
            public static class Sharing
            {
                public static void MarkAll(ModelBuilder b)
                {
                    foreach (var entityType in b.Model.GetEntityTypes())
                        b.Entity(entityType.ClrType).IsSharedAcrossTenants();
                }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Invoice> Invoices => Set<Invoice>();
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }
            """);

    [Fact]
    public Task TheAdvice_NamesTheKeyType_OrSaysTheTenantIdCannotBeAKey() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                public class Order
                {
                    public int Id { get; set; }
                    public string TenantId { get; set; } = "";
                }

                public class Refund
                {
                    public int Id { get; set; }
                    public Guid? TenantId { get; set; }
                }

                public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
                {
                    public DbSet<Invoice> Invoices => Set<Invoice>();
                    public DbSet<Order> {|#0:Orders|} => Set<Order>();
                    public DbSet<Refund> {|#1:Refunds|} => Set<Refund>();
                }
                """,
            new DiagnosticResult(Rules.TenantIdWithoutTenantEntity).WithLocation(0).WithArguments(
                "Order", "implement ITenantEntity<string>, or mark it [SharedAcrossTenants] if every tenant shares it"),
            new DiagnosticResult(Rules.TenantIdWithoutTenantEntity).WithLocation(1).WithArguments(
                "Refund",
                "its TenantId is a Guid?, which cannot be a tenant key: make it a non-nullable Guid, int, long or string " +
                "and implement ITenantEntity<TKey>, or mark the type [SharedAcrossTenants] if every tenant shares it"));

    [Fact]
    public Task ATypeParameterConstrainedToATenantOwnedType_MakesAContextOneWithTenantOwnedTypes() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Legacy
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class Archive
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class GenericDbContext<T>(DbContextOptions options) : DbContext(options) where T : class, ITenantEntity<Guid>
            {
                public DbSet<T> Items => Set<T>();
                public DbSet<Legacy> {|TNY1001:Legacy|} => Set<Legacy>();
            }

            public class MappingDbContext<T>(DbContextOptions options) : DbContext(options) where T : Invoice
            {
                public DbSet<Archive> {|TNY1001:Archives|} => Set<Archive>();

                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<T>();
            }
            """);

    [Fact]
    public Task GeneratedCode_CountsForItsMarkersAndMappings_AndNothingIsReportedInIt() =>
        Verify.AnalyzerWithFilesAsync<TenantIdWithoutTenantEntityAnalyzer>(
            Usings + """
                public class Rate
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public class Import
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public class Order
                {
                    public int Id { get; set; }
                    public Guid TenantId { get; set; }
                }

                public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
                {
                    public DbSet<Rate> Rates => Set<Rate>();
                    public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
                }
                """,
            [
                // Before the test file in source order, where a type is reported.
                ("/0/AppDbContext.g.cs", """
                    using Microsoft.EntityFrameworkCore;
                    using Tenantry.EfCore;

                    public partial class AppDbContext
                    {
                        public DbSet<Invoice> Invoices => Set<Invoice>();
                        public DbSet<Import> Imports => Set<Import>();
                        public DbSet<Order> MoreOrders => Set<Order>();

                        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Rate>().IsSharedAcrossTenants();
                    }
                    """),
            ]);
}
