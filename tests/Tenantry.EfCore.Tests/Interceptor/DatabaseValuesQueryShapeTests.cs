using System.Linq.Expressions;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Canaries for the query EF Core runs for <c>Reload</c> and <c>GetDatabaseValues</c>, which EF Core does not
/// document and which <see cref="DatabaseValuesQuery"/> recognises to keep the tenant filter on it. When a new EF Core
/// version fails here, teach <see cref="DatabaseValuesQuery"/> its shape: until then those calls read another
/// tenant's row by its key, as they do without Tenantry.
/// </summary>
public sealed class DatabaseValuesQueryShapeTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly Capture _capture = new();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Query_HasTheShapeOfThisEfCoreVersion()
    {
        var query = await CaptureAsync(db => db.Entry(new Order { Id = 1 }).GetDatabaseValuesAsync());

        // FirstOrDefault<object[]>(Select<object, object[]>(Where<object>(IgnoreQueryFilters<Order>(AsNoTracking<Order>(root)), key), e => new object[] { … }))
        var first = query.Should().BeAssignableTo<MethodCallExpression>().Subject;
        first.Method.Name.Should().Be(nameof(Queryable.FirstOrDefault));
        var select = first.Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        select.Method.Name.Should().Be(nameof(Queryable.Select));
        select.Method.GetGenericArguments().Should().Equal(typeof(object), typeof(object[]));
        var where = select.Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        where.Method.Name.Should().Be(nameof(Queryable.Where));
        var ignore = where.Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        ignore.Method.Name.Should().Be(nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters));
        ignore.Arguments.Should().ContainSingle();
        ignore.Method.GetGenericArguments().Should().Equal(typeof(Order));
        var noTracking = ignore.Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        noTracking.Method.Name.Should().Be(nameof(EntityFrameworkQueryableExtensions.AsNoTracking));
        noTracking.Arguments[0].Should().BeAssignableTo<EntityQueryRootExpression>()
            .Which.EntityType.ClrType.Should().Be(typeof(Order));

        DatabaseValuesQuery.FindIgnoreQueryFilters(select, Isolation()).Should().BeSameAs(ignore);
    }

    [Fact]
    public async Task ApplicationQueries_WithIgnoreQueryFilters_AreNotRecognised()
    {
        var query = await CaptureAsync(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == 1)
            .Select(o => new object[] { o.Id, o.TenantId }).FirstOrDefaultAsync());

        var select = ((MethodCallExpression)query).Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        DatabaseValuesQuery.FindIgnoreQueryFilters(select, Isolation()).Should().BeNull();
    }

    [Fact]
    public async Task NonTenantEntityQuery_IsNotRecognised()
    {
        var query = await CaptureAsync(db => db.Entry(new NonTenant { Id = 1 }).GetDatabaseValuesAsync());

        var select = ((MethodCallExpression)query).Arguments[0].Should().BeAssignableTo<MethodCallExpression>().Subject;
        DatabaseValuesQuery.FindIgnoreQueryFilters(select, Isolation()).Should().BeNull();
    }

    private TenantIsolation Isolation() => TenantIsolation.ForModel(_capture.Model!)!;

    // Runs the call on a context without Tenantry and returns the query EF Core compiled for it.
    private async Task<Expression> CaptureAsync(Func<TestDbContext, Task> run)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).AddInterceptors(_capture).Options;
        await using TestDbContext db = new(options);
        await db.Database.EnsureCreatedAsync();

        await run(db);

        return _capture.Query!;
    }

    private sealed class Capture : IQueryExpressionInterceptor
    {
        public Expression? Query { get; private set; }

        public Microsoft.EntityFrameworkCore.Metadata.IModel? Model { get; private set; }

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Model = eventData.Context?.Model;
            return Query = queryExpression;
        }
    }
}
