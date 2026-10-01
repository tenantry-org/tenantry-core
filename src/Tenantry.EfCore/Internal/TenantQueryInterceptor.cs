using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core interceptor that checks every query of a context that uses <c>UseTenantry()</c> when EF Core compiles it,
/// so it adds no cost to cached executions.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Checks, once per model, that every tenant-owned entity type still has the tenant filter and concurrency token (<see cref="TenantModelCheck"/>), so a model that lost one fails before it can return another tenant's rows.</item>
///   <item>Rejects an <c>ExecuteUpdate</c> that sets <c>TenantId</c> (<see cref="TenantBulkUpdateGuard"/>).</item>
///   <item>Keeps the tenant filter on the query behind <c>Reload</c> and <c>GetDatabaseValues</c> (<see cref="DatabaseValuesQuery"/>).</item>
/// </list>
/// On EF Core 10 and later, query-expression interceptors are singleton interceptors and EF Core keys its internal
/// service provider on their instances, so a new instance per context (or per host) would make EF build a new
/// internal provider each time; EF Core 8 and 9 resolve them per context. The interceptor is stateless and always used
/// through the single <see cref="Instance"/>.
/// </remarks>
internal sealed class TenantQueryInterceptor : IQueryExpressionInterceptor
{
    public static readonly TenantQueryInterceptor Instance = new();

    private TenantQueryInterceptor()
    {
    }

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        // A model without tenant-owned entity types has nothing to isolate.
        if (eventData.Context is not { } context || TenantModelCheck.Verify(context) is not { } isolation)
        {
            return queryExpression;
        }

        TenantBulkUpdateGuard.Check(queryExpression, context.Model, isolation);
        return DatabaseValuesQuery.KeepTenantFilter(queryExpression, context.Model, isolation);
    }
}
