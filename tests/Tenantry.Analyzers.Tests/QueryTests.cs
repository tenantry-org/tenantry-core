using System.Diagnostics;
using System.Text;
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
                public static List<Category> Rewrapped(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().AsEnumerable().AsQueryable().Include(c => c.Purchases).ToList();

                public static List<Purchase> Unloaded(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().AsEnumerable().SelectMany(c => c.Purchases).ToList();

                public static Task<Dictionary<int, int>> Counted(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Purchases.Count);

                public static Task<Dictionary<int, Purchase?>> First(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Purchases.FirstOrDefault());

                // A value computed from another query, which runs on its own before this one.
                public static List<Country> Taken(AppDbContext db) =>
                    db.Countries.IgnoreQueryFilters().Take(db.Orders.Where(o => o.Id > 0).Count()).ToList();

                public static List<Order> TakenFromAnIgnoringQuery(AppDbContext db) =>
                    db.Orders.OrderBy(o => o.Id).Take(db.Categories.IgnoreQueryFilters().Count()).ToList();

                public static bool Contained(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().Contains(db.Purchases.Select(p => p.Category!).First());

                // A list in memory, sent to the database as values.
                public static List<Category> Bought(AppDbContext db, List<Purchase> mine) =>
                    db.Categories.IgnoreQueryFilters().Where(c => mine.Any(p => p.CategoryId == c.Id)).ToList();

                // A purchase of the caller's, compared in the query as a value.
                public static List<Category> Matching(AppDbContext db, Purchase purchase) =>
                    db.Categories.IgnoreQueryFilters().Where(c => c.Id == purchase.CategoryId).ToList();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_FollowsCastsAndTheOtherQueryOfASetOperator() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Cast(AppDbContext db) =>
                    ((IQueryable<Category>){|TNY1002:db.Categories.IgnoreQueryFilters()|}).Include(c => c.Purchases).ToList();

                public static List<Category> Both(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Union(db.Categories.Where(c => c.Purchases.Any())).ToList();

                public static List<Category> Neither(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().Except(db.Categories.Where(c => c.IsDeleted)).ToList();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_OnALocalThatAlsoStartsAnotherQuery_SeesOnlyItsOwnQuery() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static async Task<int> SeparateFirst(AppDbContext db, int id)
                {
                    var categories = db.Categories.Where(c => c.Id == id);
                    var visible = await categories.Include(c => c.Purchases).ToListAsync();
                    var total = await categories.IgnoreQueryFilters().CountAsync();
                    return visible.Count + total;
                }

                public static async Task<int> SeparateAfter(AppDbContext db, int id)
                {
                    var categories = db.Categories.Where(c => c.Id == id);
                    var total = await categories.IgnoreQueryFilters().CountAsync();
                    var visible = await categories.Include(c => c.Purchases).ToListAsync();
                    return visible.Count + total;
                }
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_IsCheckedInItsOwnExpression_SoAQueryAcrossStatementsOrAConditionalIsNotReported() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static Task<List<Category>> IfElse(AppDbContext db, bool admin)
                {
                    IQueryable<Category> q;

                    if (admin)
                        q = db.Categories.IgnoreQueryFilters();
                    else
                        q = db.Categories.Include(c => c.Purchases);

                    return q.ToListAsync();
                }

                public static Task<List<Category>> Conditional(AppDbContext db, bool admin) =>
                    (admin ? db.Categories.IgnoreQueryFilters() : db.Categories.Include(c => c.Purchases)).ToListAsync();

                public static Task<List<Category>> ConditionalInALocal(AppDbContext db, bool admin)
                {
                    var q = admin ? db.Categories.IgnoreQueryFilters() : db.Categories.Include(c => c.Purchases);
                    return q.ToListAsync();
                }

                public static async Task<int> Guard(AppDbContext db, bool tenantView)
                {
                    var q = db.Categories.AsQueryable();

                    if (tenantView)
                    {
                        q = q.Where(c => c.Purchases.Any());
                        return await q.CountAsync();
                    }

                    return await q.IgnoreQueryFilters().CountAsync();
                }

                public static async Task<int> EarlyReturn(AppDbContext db, bool admin)
                {
                    var q = db.Categories.AsQueryable();

                    if (admin)
                    {
                        q = q.IgnoreQueryFilters();
                        return await q.CountAsync();
                    }

                    return await q.Where(c => c.Purchases.Any()).CountAsync();
                }

                public static async Task<int> Throw(AppDbContext db, bool admin)
                {
                    var q = db.Categories.AsQueryable();

                    if (admin)
                    {
                        q = q.IgnoreQueryFilters();
                        var count = await q.CountAsync();
                        throw new InvalidOperationException($"{count} categories");
                    }

                    return await q.Where(c => c.Purchases.Any()).CountAsync();
                }

                public static async Task<List<Category>> IncludeFirst(AppDbContext db, bool detail)
                {
                    IQueryable<Category> q = db.Categories;

                    if (detail)
                    {
                        q = q.Include(c => c.Purchases);
                        return await q.ToListAsync();
                    }

                    q = q.IgnoreQueryFilters();
                    return await q.ToListAsync();
                }

                public static async Task<int> InAForeach(AppDbContext db, bool[] modes)
                {
                    var total = 0;

                    foreach (var admin in modes)
                    {
                        IQueryable<Category> q = db.Categories;

                        if (admin)
                            q = q.IgnoreQueryFilters();
                        else
                            q = q.Include(c => c.Purchases);

                        total += await q.CountAsync();
                    }

                    return total;
                }

                public static async Task<List<Category>> Retry(AppDbContext db)
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        IQueryable<Category> q = db.Categories;

                        if (attempt == 0)
                            q = q.Include(c => c.Purchases);
                        else
                            q = q.IgnoreQueryFilters();

                        try
                        {
                            return await q.ToListAsync();
                        }
                        catch (TimeoutException)
                        {
                        }
                    }

                    return [];
                }

                public static async Task<List<int>> Deconstructed(AppDbContext db)
                {
                    var q = db.Categories.IgnoreQueryFilters();
                    var n = await q.CountAsync();
                    (q, _) = (db.Categories.AsQueryable(), n);
                    return await q.Select(c => c.Purchases.Count).ToListAsync();
                }
            }
            """);

    [Fact]
    public async Task ALongMethod_IsAnalysedQuickly()
    {
        // Thousands of statements and hundreds of calls on one local: each call looks at its own expression only.
        var statements = new StringBuilder();

        for (var i = 0; i < 300; i++)
        {
            statements.AppendLine($"        categories = categories.Where(c => c.Id > {i});");

            for (var j = 0; j < 12; j++)
                statements.AppendLine($"        total += {i * j};");

            statements.AppendLine("        categories = categories.IgnoreQueryFilters();");
            statements.AppendLine("        total += categories.Include(c => c.Supplier).Count();");
        }

        var stopwatch = Stopwatch.StartNew();
        await Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + $$"""
            public static class Reports
            {
                public static int Long(AppDbContext db)
                {
                    var total = 0;
                    var categories = db.Categories.AsQueryable();
            {{statements}}
                    return total;
                }
            }
            """);

        // Generous: alone it takes a few seconds, the compilation most of them, but CI runs every test project at once,
        // where it took over 30 seconds. Following locals through the method took 8 minutes.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(2), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public Task IgnoreQueryFilters_SeesNoQueryReturnedFromOrPassedToAMethod() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Elsewhere(AppDbContext db) => AllCategories(db).Include(c => c.Purchases).ToList();

                private static IQueryable<Category> AllCategories(AppDbContext db) => db.Categories.IgnoreQueryFilters();

                public static List<Category> Passed(AppDbContext db) => WithPurchases(db.Categories.IgnoreQueryFilters());

                private static List<Category> WithPurchases(IQueryable<Category> categories) =>
                    categories.Include(c => c.Purchases).ToList();

                public static List<Category> Built(AppDbContext db) =>
                    Bought(db.Purchases.Where(p => p.Total > 0)).IgnoreQueryFilters().ToList();

                private static IQueryable<Category> Bought(IQueryable<Purchase> purchases) =>
                    purchases.Select(p => p.Category!);
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_OnATypeParameterConstrainedToATenantOwnedType_IsReported() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public class Repository<T>(DbContext db) where T : class, ITenantEntity<Guid>
            {
                public IQueryable<T> Everyone() => {|TNY1002:db.Set<T>().IgnoreQueryFilters()|};
            }

            public class PurchaseRepository<T>(DbContext db) where T : Purchase
            {
                public IQueryable<T> Everyone() => {|TNY1002:db.Set<T>().IgnoreQueryFilters()|};
            }

            public class SharedRepository<T>(DbContext db) where T : class
            {
                public IQueryable<T> Everything() => db.Set<T>().IgnoreQueryFilters();
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
