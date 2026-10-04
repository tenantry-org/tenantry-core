using Tenantry.EfCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>TNY1002, a query that ignores the tenant filter, and TNY1003, raw SQL.</summary>
public sealed class QueryTests
{
    private const string Model = """
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
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

        public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Country> Countries => Set<Country>();
        }

        """;

    [Fact]
    public Task IgnoreQueryFilters_OnATenantOwnedSet_IsReported_AndOnASharedOneIsNot() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static int Orders(AppDbContext db) => {|TNY1002:db.Orders.IgnoreQueryFilters()|}.Count();

                public static int OrdersOver(AppDbContext db) => {|TNY1002:db.Orders.Where(o => o.Id > 1).IgnoreQueryFilters()|}.Count();

                public static int Countries(AppDbContext db) => db.Countries.IgnoreQueryFilters().Count();
            }
            """);

#if NET10_0_OR_GREATER
    [Fact]
    public Task IgnoreQueryFilters_ByName_IsReportedOnlyWhenItNamesTheTenantFilter() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static int Deleted(AppDbContext db) => db.Orders.IgnoreQueryFilters(["SoftDelete"]).Count();

                public static int Everyone(AppDbContext db) =>
                    {|TNY1002:db.Orders.IgnoreQueryFilters([TenantryQueryFilters.Tenant])|}.Count();

                public static int ByVariable(AppDbContext db, string[] names) => db.Orders.IgnoreQueryFilters(names).Count();
            }
            """);
#endif

    [Fact]
    public Task RawSqlOnTheDatabase_IsReported_AndFromSqlOnASetIsNot() =>
        Verify.AnalyzerAsync<RawSqlAnalyzer>(Model + """
            public static class Maintenance
            {
                public static Task<int> Purge(AppDbContext db) =>
                    {|TNY1003:db.Database.ExecuteSqlRawAsync("DELETE FROM Orders")|};

                public static int Count(AppDbContext db) =>
                    {|TNY1003:db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Orders")|}.Single();

                public static int Touch(AppDbContext db, int id) =>
                    {|TNY1003:db.Database.ExecuteSql($"UPDATE Orders SET Id = Id WHERE Id = {id}")|};

                public static int Filtered(AppDbContext db) => db.Orders.FromSqlRaw("SELECT * FROM Orders").Count();
            }
            """);
}
