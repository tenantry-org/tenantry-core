using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Rejects <c>ExecuteUpdate</c> queries that set <c>TenantId</c> on a tenant-scoped entity.
/// </summary>
/// <remarks>
/// The tenant query filter limits which rows a bulk update touches, but not the values it writes, and bulk
/// updates bypass the <c>SaveChanges</c> interceptor. Without this guard,
/// <c>ExecuteUpdate(s =&gt; s.SetProperty(o =&gt; o.TenantId, other))</c> would move every row the current
/// tenant can see into another tenant. The check runs when EF Core compiles the query, so it adds no cost to
/// cached executions. Only the properties being set are inspected: reading <c>TenantId</c> as a value is
/// allowed.
/// <para>
/// EF Core keys its internal service provider on query-expression interceptor instances, so a new instance
/// per context (or per host) would make EF build a new internal provider each time. The guard is stateless
/// and always used through the single <see cref="Instance"/>.
/// </para>
/// </remarks>
internal sealed class TenantBulkUpdateGuard<TKey> : IQueryExpressionInterceptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private const string TenantIdProperty = nameof(ITenantScoped<>.TenantId);

    public static readonly TenantBulkUpdateGuard<TKey> Instance = new();

    private TenantBulkUpdateGuard()
    {
    }

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        new ExecuteUpdateVisitor().Visit(queryExpression);
        return queryExpression;
    }

    private static bool SetsTenantId(LambdaExpression propertySelector) =>
        propertySelector.Parameters is [{ } entity] &&
        typeof(ITenantScoped<TKey>).IsAssignableFrom(entity.Type) &&
        StripConvert(propertySelector.Body) is MemberExpression { Member.Name: TenantIdProperty } member &&
        StripConvert(member.Expression) is ParameterExpression parameter &&
        parameter == entity;

    private static Expression? StripConvert(Expression? expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            expression = convert.Operand;
        }

        return expression;
    }

    private static LambdaExpression? AsLambda(Expression expression) => expression switch
    {
        LambdaExpression lambda => lambda,
        UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } => quoted,
        ConstantExpression { Value: LambdaExpression constant } => constant,
        _ => null,
    };

    private sealed class ExecuteUpdateVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method is { Name: "ExecuteUpdate", DeclaringType.Namespace: "Microsoft.EntityFrameworkCore" })
            {
                SetterSelectorCollector setters = new();

                foreach (var argument in node.Arguments.Skip(1))
                {
                    setters.Visit(argument);
                }

                if (setters.Selectors.FirstOrDefault(SetsTenantId) is { } offending)
                {
                    var entityTypeName = offending.Parameters[0].Type.Name;

                    throw new TenantIsolationViolationException(
                        entityTypeName,
                        $"ExecuteUpdate cannot set TenantId on tenant-scoped entity '{entityTypeName}': that would " +
                        "move rows into another tenant. Move data between tenants with explicit, reviewed SQL.");
                }
            }

            return base.VisitMethodCall(node);
        }
    }

    // Collects the property selector (first argument) of each ExecuteUpdate setter. EF Core 8 and 9 express
    // setters as SetPropertyCalls<T>.SetProperty(selector, value) calls; EF Core 10 passes an array of
    // Tuple<LambdaExpression, Expression>(selector, value).
    private sealed class SetterSelectorCollector : ExpressionVisitor
    {
        public List<LambdaExpression> Selectors { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "SetProperty" && node.Arguments.Count == 2 && AsLambda(node.Arguments[0]) is { } selector)
            {
                Selectors.Add(selector);
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitNew(NewExpression node)
        {
            if (node.Type.IsGenericType &&
                node.Type.GetGenericTypeDefinition() == typeof(Tuple<,>) &&
                AsLambda(node.Arguments[0]) is { } selector)
            {
                Selectors.Add(selector);
            }

            return base.VisitNew(node);
        }
    }
}
