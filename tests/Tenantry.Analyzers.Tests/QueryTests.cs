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

                public static Task<Dictionary<int, Purchase?>> First(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Purchases.FirstOrDefault());

                // A list in memory, sent to the database as values.
                public static List<Category> Bought(AppDbContext db, List<Purchase> mine) =>
                    db.Categories.IgnoreQueryFilters().Where(c => mine.Any(p => p.CategoryId == c.Id)).ToList();

                // A purchase of the caller's, compared in the query as a value.
                public static List<Category> Matching(AppDbContext db, Purchase purchase) =>
                    db.Categories.IgnoreQueryFilters().Where(c => c.Id == purchase.CategoryId).ToList();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_FollowsCastsConditionalsAndTheOtherQueryOfASetOperator() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Cast(AppDbContext db) =>
                    ((IQueryable<Category>){|TNY1002:db.Categories.IgnoreQueryFilters()|}).Include(c => c.Purchases).ToList();

                public static List<Category> Either(AppDbContext db, bool all) =>
                    (all ? {|TNY1002:db.Categories.IgnoreQueryFilters()|} : db.Categories).Include(c => c.Purchases).ToList();

                public static List<Category> FromEither(AppDbContext db, bool all) =>
                    {|TNY1002:(all ? db.Categories.Include(c => c.Purchases) : db.Categories.AsQueryable()).IgnoreQueryFilters()|}.ToList();

                public static List<Category> Both(AppDbContext db) =>
                    {|TNY1002:db.Categories.IgnoreQueryFilters()|}.Union(db.Categories.Where(c => c.Purchases.Any())).ToList();

                public static List<Category> Neither(AppDbContext db) =>
                    db.Categories.IgnoreQueryFilters().Except(db.Categories.Where(c => c.IsDeleted)).ToList();
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_FollowsALocalTheQueryIsKeptIn() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Declared(AppDbContext db)
                {
                    var categories = {|TNY1002:db.Categories.IgnoreQueryFilters()|};
                    return categories.Include(c => c.Purchases).ToList();
                }

                public static List<Category> Conditional(AppDbContext db, bool all)
                {
                    var categories = db.Categories.AsQueryable();

                    if (all)
                        categories = {|TNY1002:categories.IgnoreQueryFilters()|};

                    categories = categories.Where(c => !c.IsDeleted);
                    categories = categories.Include(c => c.Purchases);
                    return categories.ToList();
                }

                // The filters are ignored for the whole query, so an Include before the call counts.
                public static List<Category> IncludedFirst(AppDbContext db)
                {
                    IQueryable<Category> categories = db.Categories.Include(c => c.Purchases);
                    categories = {|TNY1002:categories.IgnoreQueryFilters()|};
                    return categories.ToList();
                }

                public static List<Category> AnotherLocal(AppDbContext db)
                {
                    var all = db.Categories.IgnoreQueryFilters();
                    var mine = db.Categories.Include(c => c.Purchases);
                    return all.Concat(db.Categories.Where(c => !c.IsDeleted)).ToList().Concat(mine).ToList();
                }

                // Following stops where the local is given a query that does not start from it.
                public static List<Category> Replaced(AppDbContext db)
                {
                    var categories = db.Categories.IgnoreQueryFilters();
                    categories = db.Categories.Where(c => !c.IsDeleted);
                    return categories.Include(c => c.Purchases).ToList();
                }

                public static List<Category> ReplacedBefore(AppDbContext db)
                {
                    IQueryable<Category> categories = db.Categories.Include(c => c.Purchases);
                    categories = db.Categories.Where(c => !c.IsDeleted);
                    return categories.IgnoreQueryFilters().ToList();
                }

                // A query run before the call is another query.
                public static int Earlier(AppDbContext db)
                {
                    var categories = db.Categories.AsQueryable();
                    var bought = categories.Include(c => c.Purchases).Count();
                    categories = categories.IgnoreQueryFilters();
                    return bought + categories.Count();
                }

                // A local a lambda assigns is not followed.
                public static List<Category> Captured(AppDbContext db)
                {
                    var categories = db.Categories.IgnoreQueryFilters();
                    Action narrow = () => categories = categories.Where(c => !c.IsDeleted);
                    return categories.Include(c => c.Purchases).ToList();
                }
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
    public Task IgnoreQueryFilters_OnOneBranch_IsNotJoinedToAQueryOnAnother_OutsideALoop() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> IfElse(AppDbContext db, bool all)
                {
                    var categories = db.Categories.AsQueryable();

                    if (all)
                        categories = categories.IgnoreQueryFilters();
                    else
                        categories = categories.Include(c => c.Purchases);

                    return categories.ToList();
                }

                public static List<Category> ElseIf(AppDbContext db, bool all)
                {
                    var categories = db.Categories.AsQueryable();

                    if (all)
                        categories = categories.Include(c => c.Purchases);
                    else
                        categories = categories.IgnoreQueryFilters();

                    return categories.ToList();
                }

                public static List<Category> Switch(AppDbContext db, int mode)
                {
                    var categories = db.Categories.AsQueryable();

                    switch (mode)
                    {
                        case 1:
                            categories = categories.IgnoreQueryFilters();
                            break;
                        default:
                            categories = categories.Include(c => c.Purchases);
                            break;
                    }

                    return categories.ToList();
                }

                // The condition runs before the branch.
                public static List<Category> InTheCondition(AppDbContext db)
                {
                    var categories = db.Categories.AsQueryable();

                    if ((categories = {|TNY1002:categories.IgnoreQueryFilters()|}).Any())
                        return categories.Include(c => c.Purchases).ToList();

                    return [];
                }

                // Both branches can run, one after the other.
                public static List<Category> Loop(AppDbContext db, bool[] steps)
                {
                    var categories = db.Categories.AsQueryable();

                    foreach (var all in steps)
                    {
                        if (all)
                            categories = {|TNY1002:categories.IgnoreQueryFilters()|};
                        else
                            categories = categories.Include(c => c.Purchases);
                    }

                    return categories.ToList();
                }
            }
            """);

    [Fact]
    public Task IgnoreQueryFilters_FollowsALocalThroughAConditionalReassignment() =>
        Verify.AnalyzerAsync<IgnoreQueryFiltersAnalyzer>(Model + """
            public static class Reports
            {
                public static List<Category> Narrowed(AppDbContext db, bool live)
                {
                    var categories = {|TNY1002:db.Categories.IgnoreQueryFilters()|};
                    categories = live ? categories.Where(c => !c.IsDeleted) : categories;
                    return categories.Include(c => c.Purchases).ToList();
                }

                public static List<Category> Replaced(AppDbContext db, bool live)
                {
                    var categories = db.Categories.IgnoreQueryFilters();
                    categories = live ? db.Categories.Where(c => !c.IsDeleted) : categories;
                    return categories.Include(c => c.Purchases).ToList();
                }

                // A query used as a condition does not continue into what the conditional chooses.
                public static List<Category> AsACondition(AppDbContext db) =>
                    (db.Categories.IgnoreQueryFilters().Any() ? db.Categories.AsQueryable() : db.Categories.Where(c => !c.IsDeleted))
                        .Include(c => c.Purchases)
                        .ToList();
            }
            """);

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
