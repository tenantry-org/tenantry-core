using Tenantry.EfCore.Analyzers;
using Tenantry.EfCore.CodeFixes;

namespace Tenantry.Analyzers.Tests;

/// <summary>TNY1001: an entity with a TenantId that does not implement ITenantEntity&lt;TKey&gt;.</summary>
public sealed class TenantEntityTests
{
    private const string Usings = """
        using System;
        using Microsoft.EntityFrameworkCore;
        using Tenantry;
        using Tenantry.EfCore;

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

            public class Invoice : Owned
            {
                public int Id { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => {|TNY1001:modelBuilder.Entity<Invoice>()|};
            }
            """);

    [Fact]
    public Task TenantOwnedSharedAndTenantTypes_AreNotReported() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order : TenantEntity<Guid>
            {
                public int Id { get; set; }
            }

            public class Line : ITenantEntity<Guid>
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            [SharedAcrossTenants]
            public class Import
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            // A tenant registry: TenantId is its key, by EF Core's convention.
            public class Tenant
            {
                public Guid TenantId { get; set; }
                public string Name { get; set; } = "";
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
                public DbSet<Order> Orders => Set<Order>();
                public DbSet<Line> Lines => Set<Line>();
                public DbSet<Import> Imports => Set<Import>();
                public DbSet<Tenant> Tenants => Set<Tenant>();
                public DbSet<Customer> Customers => Set<Customer>();
                public DbSet<Country> Countries => Set<Country>();
            }
            """);

    [Fact]
    public Task ADbSetOutsideADbContext_IsNotReported() =>
        Verify.AnalyzerAsync<TenantIdWithoutTenantEntityAnalyzer>(Usings + """
            public class Order
            {
                public Guid TenantId { get; set; }
            }

            public class Holder
            {
                public DbSet<Order>? Orders { get; set; }
            }
            """);

    [Fact]
    public Task TheFix_ImplementsITenantEntity_WithTheTenantIdsType() =>
        Verify.CodeFixAsync<TenantIdWithoutTenantEntityAnalyzer, ImplementTenantEntityCodeFix>(
            """
            using System;
            using Microsoft.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Order> {|TNY1001:Orders|} => Set<Order>();
            }
            """,
            """
            using System;
            using Microsoft.EntityFrameworkCore;
            using Tenantry;

            public class Order : ITenantEntity<Guid>
            {
                public int Id { get; set; }
                public Guid TenantId { get; set; }
            }

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders => Set<Order>();
            }
            """);
}
