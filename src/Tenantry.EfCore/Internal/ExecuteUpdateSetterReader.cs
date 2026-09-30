using System.Linq.Expressions;
#if !EFCORE10_OR_GREATER
using Microsoft.EntityFrameworkCore.Query;
#endif

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Reads the property selectors of an <c>ExecuteUpdate</c> query's setters, in the expression shape of the EF Core
/// major version this build targets.
/// </summary>
/// <remarks>
/// EF Core does not document these shapes, and they changed in EF Core 10. <c>ExecuteUpdateShapeTests</c> pins the
/// shape of each supported version, so a new EF Core version that changes it fails a focused test. Until this reader
/// handles the new shape it returns no selectors, and <see cref="TenantBulkUpdateGuard"/> rejects the query
/// rather than let a setter through unchecked.
/// </remarks>
internal static class ExecuteUpdateSetterReader
{
    /// <summary>Whether <paramref name="call"/> is EF Core's <c>ExecuteUpdate</c> query operator.</summary>
    /// <remarks><c>ExecuteUpdateAsync</c> reaches query compilation as the same operator.</remarks>
    public static bool IsExecuteUpdate(MethodCallExpression call) =>
        call.Method is { Name: "ExecuteUpdate", DeclaringType.Namespace: "Microsoft.EntityFrameworkCore" };

    /// <summary>
    /// The property selector of each setter, in order, or an empty list when the setters are not in the shape this
    /// EF Core version uses.
    /// </summary>
    public static IReadOnlyList<LambdaExpression> ReadSelectors(MethodCallExpression executeUpdate)
    {
        List<LambdaExpression> selectors = [];

#if EFCORE10_OR_GREATER
        // EF Core 10: ExecuteUpdate(source, IReadOnlyList<ITuple>), the list being
        // new ITuple[] { new Tuple<Delegate, object>(selector, value), … }.
        if (executeUpdate.Arguments is not [_, NewArrayExpression { NodeType: ExpressionType.NewArrayInit } setters])
        {
            return [];
        }

        foreach (var setter in setters.Expressions)
        {
            if (setter is not NewExpression { Arguments: [var selector, _] } tuple ||
                tuple.Type != typeof(Tuple<Delegate, object>) ||
                AsLambda(selector) is not { } lambda)
            {
                return [];
            }

            selectors.Add(lambda);
        }
#else
        // EF Core 8 and 9: ExecuteUpdate(source, s => s.SetProperty(selector, value).SetProperty(…)), a chain of
        // SetPropertyCalls<T>.SetProperty calls on the lambda's parameter. EF Core also accepts a chain composed from
        // other setter lambdas by invoking them (s => inner.Invoke(s)), which is read with the invocation inlined.
        if (executeUpdate.Arguments is not [_, var argument] || AsLambda(argument) is not { Parameters: [var calls] } setters)
        {
            return [];
        }

        var current = setters.Body;

        while (current != calls)
        {
            if (current is InvocationExpression { Arguments: [var invokedWith] } invocation &&
                AsLambda(invocation.Expression) is { Parameters: [var parameter] } inner)
            {
                current = ParameterReplacer.Replace(inner.Body, parameter, invokedWith);
                continue;
            }

            if (current is not MethodCallExpression { Method.Name: "SetProperty", Object: { } previous, Arguments: [var selector, _] } call ||
                call.Method.DeclaringType is not { IsGenericType: true } declaringType ||
                declaringType.GetGenericTypeDefinition() != typeof(SetPropertyCalls<>) ||
                AsLambda(selector) is not { } lambda)
            {
                return [];
            }

            selectors.Add(lambda);
            current = previous;
        }

        // The chain was read from the last call back to the first.
        selectors.Reverse();
#endif

        return selectors;
    }

    /// <summary>
    /// The lambda an expression holds, however the compiler or EF Core wrapped it (quoted, as a constant, or bare), or
    /// <see langword="null"/>.
    /// </summary>
    public static LambdaExpression? AsLambda(Expression expression) => expression switch
    {
        LambdaExpression lambda => lambda,
        UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } => quoted,
        ConstantExpression { Value: LambdaExpression constant } => constant,
        _ => null,
    };
#if !EFCORE10_OR_GREATER

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        public static Expression Replace(Expression expression, ParameterExpression parameter, Expression replacement) =>
            new ParameterReplacer(parameter, replacement).Visit(expression);

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
#endif
}
