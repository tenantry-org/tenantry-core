using Microsoft.CodeAnalysis.Testing;
using Tenantry.EfCore.Analyzers;

namespace Tenantry.Analyzers.Tests;

/// <summary>TNY1002, a query that ignores the tenant filter, and TNY1003, raw SQL.</summary>
public sealed class QueryTests
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
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

        // Shared by every tenant, with a filter of the application's own, such as a soft delete.
        public class Supplier
        {
            public int Id { get; set; }
            public List<Category> Categories { get; set; } = [];
        }

        public class Category
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsDeleted { get; set; }
            public Supplier? Supplier { get; set; }
            public ICollection<Purchase> Purchases { get; set; } = [];
        }

        public class Purchase : TenantEntity<Guid>
        {
            public int Id { get; set; }
            public int CategoryId { get; set; }
            public decimal Total { get; set; }
            public Category? Category { get; set; }
        }

        public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Country> Countries => Set<Country>();
            public DbSet<Supplier> Suppliers => Set<Supplier>();
            public DbSet<Category> Categories => Set<Category>();
            public DbSet<Purchase> Purchases => Set<Purchase>();
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

    [Fact]
    public Task IgnoreQueryFilters_OnASharedSet_IsReported_WhenTheQueryIncludesATenantOwnedType() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(
            Model + """
                public static class Reports
                {
                    public static List<Category> After(AppDbContext db) =>
                        {|#0:db.Categories.IgnoreQueryFilters()|}.Include(c => c.Purchases).ToList();

                    public static List<Category> Before(AppDbContext db) =>
                        {|TNY1002:db.Categories.Include(c => c.Purchases).IgnoreQueryFilters()|}.ToList();

                    public static List<Category> Filtered(AppDbContext db) =>
                        {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Include(c => c.Purchases.Where(p => p.Total > 0)).ToList();

                    public static List<Category> ByName(AppDbContext db) =>
                        {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Include("Purchases").ToList();

                    public static List<Supplier> Then(AppDbContext db) =>
                        {|TNY1002:db.Suppliers.IgnoreQueryFilters()|}.Include(s => s.Categories).ThenInclude(c => c.Purchases).ToList();

                    public static List<Supplier> ThenByName(AppDbContext db) =>
                        {|TNY1002:db.Suppliers.IgnoreQueryFilters()|}.Include("Categories.Purchases").ToList();
                }
                """,
            new DiagnosticResult(Rules.IgnoreQueryFilters).WithLocation(0).WithArguments("Purchase"));

    [Fact]
    public Task IgnoreQueryFilters_IsReported_WhenTheQuerySelectsOrJoinsATenantOwnedType() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static Task<decimal> Spent(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Select(c => c.Purchases.Sum(p => p.Total)).SumAsync();

                public static object Projected(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Select(c => new { c.Name, c.Purchases }).ToList();

                public static List<Purchase> Flattened(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.SelectMany(c => c.Purchases).ToList();

                public static List<string> Joined(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}
                        .Join(db.Purchases, c => c.Id, p => p.CategoryId, (c, p) => c.Name)
                        .ToList();

                // EF Core ignores the filters for the whole query, wherever the call is.
                public static List<string> Inner(AppDbContext db) =>
                    db.Purchases.Join({|TNY1002:db.Categories.IgnoreQueryFilters()|}, p => p.CategoryId, c => c.Id, (p, c) => c.Name).ToList();

                public static int Bought(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Count(c => db.Purchases.Any(p => p.CategoryId == c.Id));

                public static int WhilePurchased(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Count(c => db.Purchases.Any());

                public static int Used(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Where(c => c.Purchases.Any()).ExecuteDelete();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_IsNotReported_WhenTheQueryReadsOnlySharedTypes() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<string> Names(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().Include(c => c.Supplier).Where(c => !c.IsDeleted).Select(c => c.Name).ToList();

                public static List<Supplier> ByName(AppDbContext db) =>
                    db.Suppliers.IgnoreQueryFilters().Include("Categories").Include("NotANavigation").ToList();

                // In memory, after the query: Purchases is not loaded by it.
                public static List<Purchase> Unloaded(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().AsEnumerable().SelectMany(c => c.Purchases).ToList();

                public static Task<Dictionary<int, int>> Counted(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Purchases.Count);

                // A purchase of the caller's, compared in the query as a value.
                public static List<Category> Matching(AppDbContext db, Purchase purchase) =>
                    db.Categories.IgnoreQueryFilters().Where(c => c.Id == purchase.CategoryId).ToList();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_SeesOnlyTheExpressionItIsIn() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Later(AppDbContext db)
                {
                    var categories = db.Categories.IgnoreQueryFilters();
                    return categories.Include(c => c.Purchases).ToList();
                }

                public static List<Category> Elsewhere(AppDbContext db) => AllCategories(db).Include(c => c.Purchases).ToList();

                private static IQueryable<Category> AllCategories(AppDbContext db) => db.Categories.IgnoreQueryFilters();
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

                public static List<Category> Live(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters(["SoftDelete"]).Include(c => c.Purchases).ToList();

                public static List<Category> EveryTenants(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters([TenantryQueryFilters.Tenant])|}.Include(c => c.Purchases).ToList();
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
