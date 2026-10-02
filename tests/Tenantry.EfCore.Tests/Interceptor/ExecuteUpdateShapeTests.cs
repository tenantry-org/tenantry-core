using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Tenantry;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

// ReSharper disable EntityFramework.ClientSideDbFunctionCall (see BulkAndRawWriteBoundaryTests)

/// <summary>
/// Canaries for the expression shape EF Core gives <c>ExecuteUpdate</c> setters, which EF Core does not document
/// and which <see cref="ExecuteUpdateSetterReader"/> reads for the bulk-update guard. When a new EF Core version
/// fails here, teach the reader its shape: until then the guard rejects every <c>ExecuteUpdate</c>.
/// </summary>
public sealed class ExecuteUpdateShapeTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly Capture _capture = new();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Setters_HaveTheShapeOfThisEfCoreVersion()
    {
        var call = await CaptureExecuteUpdateAsync(orders => orders.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Description, "fixed")
            .SetProperty(o => EF.Property<string>(o, nameof(Order.Description)), o => o.Description + "!")));

        ExecuteUpdateSetterReader.IsExecuteUpdate(call).Should().BeTrue();
#if EFCORE10_OR_GREATER
        call.Method.DeclaringType.Should().Be(typeof(EntityFrameworkQueryableExtensions));
        var setters = call.Arguments[1].Should().BeAssignableTo<NewArrayExpression>().Subject;
        setters.Type.Should().Be<ITuple[]>();
        setters.Expressions.Should().HaveCount(2)
            .And.AllSatisfy(setter => setter.Should().BeAssignableTo<NewExpression>()
                .Which.Type.Should().Be<Tuple<Delegate, object>>());
#else
#if NET9_0
        call.Method.DeclaringType.Should().Be(typeof(EntityFrameworkQueryableExtensions));
#else
        call.Method.DeclaringType.Should().Be(typeof(RelationalQueryableExtensions));
#endif
        var setters = call.Arguments[1].Should().BeAssignableTo<UnaryExpression>()
            .Which.Operand.Should().BeAssignableTo<LambdaExpression>().Subject;
        setters.Parameters.Should().ContainSingle().Which.Type.Should().Be<SetPropertyCalls<Order>>();
        setters.Body.Should().BeAssignableTo<MethodCallExpression>().Which.Method.Name.Should().Be("SetProperty");
#endif
    }

    [Fact]
    public async Task Reader_ReturnsEverySelectorInOrder()
    {
        var call = await CaptureExecuteUpdateAsync(orders => orders.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Description, "fixed")
            .SetProperty(o => EF.Property<string>(o, nameof(Order.TenantId)), "other")
            .SetProperty(o => o.Description, o => o.Description + "!")));

        ExecuteUpdateSetterReader.ReadSelectors(call).Select(selector => selector.Body.ToString())
            .Should().Equal("o.Description", "Property(o, \"TenantId\")", "o.Description");
    }

    [Fact]
    public async Task ExecuteUpdate_WithSettersInAnUnknownShape_IsRejected()
    {
        var call = await CaptureExecuteUpdateAsync(orders => orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "fixed")));

#if EFCORE10_OR_GREATER
        var unknown = Expression.Call(call.Method, call.Arguments[0], Expression.Constant(Array.Empty<ITuple>(), typeof(IReadOnlyList<ITuple>)));
#else
        var calls = Expression.Parameter(typeof(SetPropertyCalls<Order>), "s");
        var unknown = Expression.Call(call.Method, call.Arguments[0], Expression.Quote(Expression.Lambda(calls, calls)));
#endif

        ExecuteUpdateSetterReader.ReadSelectors(unknown).Should().BeEmpty();
        FluentActions.Invoking(() => TenantBulkUpdateGuard.Check(unknown, model: null, TenantIsolation.ForModel(_capture.Model!)!))
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Tenantry cannot read the setters of this ExecuteUpdate on EF Core *");
    }

#if !EFCORE10_OR_GREATER
    [Fact]
    public async Task SettersComposedWithInvoke_AreReadAndChecked()
    {
        // EF Core 8 and 9 accept a setter lambda that invokes another; the guard reads through the invocation.
        await using var db = await DbContextFactory.CreateContextAsync(TestTenantContext.For("acme"), _connection);

        (await db.Orders.ExecuteUpdateAsync(Invoking(s => s.SetProperty(o => o.Description, "fixed")), cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        await db.Awaiting(context => context.Orders.ExecuteUpdateAsync(Invoking(s => s.SetProperty(o => o.TenantId, "other"))))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("ExecuteUpdate cannot set TenantId*");
    }

    // s => inner(s)
    private static Expression<Func<SetPropertyCalls<Order>, SetPropertyCalls<Order>>> Invoking(
        Expression<Func<SetPropertyCalls<Order>, SetPropertyCalls<Order>>> inner)
    {
        var calls = Expression.Parameter(typeof(SetPropertyCalls<Order>), "s");
        return Expression.Lambda<Func<SetPropertyCalls<Order>, SetPropertyCalls<Order>>>(Expression.Invoke(inner, calls), calls);
    }
#endif

    // Runs the query on a context without Tenantry's guard and returns the ExecuteUpdate call EF Core compiled.
    private async Task<MethodCallExpression> CaptureExecuteUpdateAsync(Func<IQueryable<Order>, Task> run)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).AddInterceptors(_capture).Options;
        await using TestDbContext db = new(options);
        await db.Database.EnsureCreatedAsync();

        await run(db.Orders);

        return _capture.Query.Should().BeAssignableTo<MethodCallExpression>().Subject;
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
