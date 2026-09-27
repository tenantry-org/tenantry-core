using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
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
/// EF Core resolves each setter's property selector through the query's projections, so
/// <c>Select(o =&gt; new { T = o.TenantId }).ExecuteUpdate(s =&gt; s.SetProperty(x =&gt; x.T, other))</c>
/// sets <c>Order.TenantId</c>. The guard resolves selectors the same way (through <c>Select</c>, <c>Join</c>
/// and <c>SelectMany</c> result selectors and element-preserving operators such as <c>Where</c>) and then
/// checks the entity property the setter lands on, whether it is named by member access or by
/// <c>EF.Property</c>. It fails closed: a setter whose <c>EF.Property</c> name cannot be read, or that lands on
/// a projection it cannot see through, is rejected.
/// </para>
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
        new ExecuteUpdateVisitor(eventData.Context?.Model).Visit(queryExpression);
        return queryExpression;
    }

    private static bool IsTenantScoped(Type? type) =>
        type is not null && typeof(ITenantScoped<TKey>).IsAssignableFrom(type);

    private static bool IsEfProperty(MethodCallExpression call) =>
        call.Method is { Name: nameof(EF.Property), IsGenericMethod: true } && call.Method.DeclaringType == typeof(EF);

    private static Expression? StripConvert(Expression? expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } convert)
        {
            expression = convert.Operand;
        }

        return expression;
    }

    // The tenant-scoped entity type a setter's instance expression refers to, looking through casts (which
    // matter for inheritance: ((TenantScopedDerived)baseEntity).TenantId) and falling back to the member's
    // declaring type. Prefers the innermost concrete type, so an interface cast still names the entity.
    private static Type? TenantScopedEntityType(Expression instance, MemberInfo? member)
    {
        Type? found = null;

        for (Expression? current = instance; current is not null; current = current is UnaryExpression
                 { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } convert
                 ? convert.Operand
                 : null)
        {
            if (IsTenantScoped(current.Type) && (found is null || found.IsInterface || !current.Type.IsInterface))
            {
                found = current.Type;
            }
        }

        if (found is null or { IsInterface: true } && IsTenantScoped(member?.DeclaringType) && !member!.DeclaringType!.IsInterface)
        {
            return member.DeclaringType;
        }

        return found ?? (IsTenantScoped(member?.DeclaringType) ? StripConvert(instance)!.Type : null);
    }

    // The property name passed to EF.Property: a constant, or a captured variable EF has not yet inlined.
    // Returns null when it cannot be read without running user code.
    private static string? ReadPropertyName(Expression expression) => StripConvert(expression) switch
    {
        ConstantExpression { Value: string name } => name,
        MemberExpression { Expression: ConstantExpression { Value: var target }, Member: FieldInfo field } => field.GetValue(target) as string,
        MemberExpression { Expression: null, Member: FieldInfo { IsStatic: true } field } => field.GetValue(null) as string,
        _ => null,
    };

    private static LambdaExpression? AsLambda(Expression expression) => expression switch
    {
        LambdaExpression lambda => lambda,
        UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } => quoted,
        ConstantExpression { Value: LambdaExpression constant } => constant,
        _ => null,
    };

    // The element type of the sequence types LINQ operators return. Deliberately limited to the generic
    // interfaces themselves (no interface scanning), which is all the guard needs to spot pass-through operators.
    private static Type? SequenceElementType(Type type)
    {
        if (!type.IsGenericType)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(IQueryable<>) || definition == typeof(IOrderedQueryable<>) ||
               definition == typeof(IEnumerable<>) || definition == typeof(IOrderedEnumerable<>) ||
               definition == typeof(IIncludableQueryable<,>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private sealed class ExecuteUpdateVisitor(IModel? model) : ExpressionVisitor
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

                foreach (var selector in setters.Selectors)
                {
                    var element = ElementOf(node.Arguments[0], selector.Parameters[0].Type);
                    CheckSetter(Substitution.Apply(selector, element));
                }
            }

            return base.VisitMethodCall(node);
        }

        // The property a setter lands on once its selector is resolved through the query's projections.
        private void CheckSetter(Expression target)
        {
            (Expression? instance, MemberInfo? member, string? name) = StripConvert(target) switch
            {
                MemberExpression access => (access.Expression, access.Member, access.Member.Name),
                MethodCallExpression call when IsEfProperty(call) => (call.Arguments[0], null, ReadPropertyName(call.Arguments[1])),
                _ => (null, null, null),
            };

            if (instance is null)
            {
                // Not a property access (EF Core rejects it as a setter), or a static member.
                return;
            }

            if (TenantScopedEntityType(instance, member) is { } entityType)
            {
                if (name is null)
                {
                    Reject(entityType, $"ExecuteUpdate cannot check a setter on tenant-scoped entity '{entityType.Name}': " +
                                       "its EF.Property name could not be read, so it may set TenantId.");
                }

                if (name == TenantIdProperty)
                {
                    Reject(entityType, $"ExecuteUpdate cannot set TenantId on tenant-scoped entity '{entityType.Name}': " +
                                       "that would move rows into another tenant.");
                }

                return;
            }

            // A projection element the guard could not resolve (for example after GroupBy). Unless it is itself
            // an entity, EF Core may map this member to any column, including TenantId, so fail closed.
            if (StripConvert(instance) is ParameterExpression unresolved && !IsEntityType(unresolved.Type))
            {
                Reject(unresolved.Type, "ExecuteUpdate cannot check which entity property a setter sets through " +
                                        $"'{unresolved.Type.Name}', so it may set TenantId.");
            }
        }

        private bool IsEntityType(Type type) => model?.GetEntityTypes().Any(entity => entity.ClrType == type) == true;

        private static void Reject(Type type, string problem) =>
            throw new TenantIsolationViolationException(
                type.Name,
                $"{problem} Set properties on the entity itself, and move data between tenants with explicit, " +
                "reviewed SQL.");

        // The element a query produces, expressed in terms of the elements of its sources. A source the guard
        // does not model (a DbSet root, GroupBy, …) becomes a placeholder parameter of the expected element type.
        private static Expression ElementOf(Expression source, Type elementType)
        {
            if (source is MethodCallExpression { Arguments.Count: > 0 } call)
            {
                if (call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
                {
                    switch (call.Method.Name)
                    {
                        case nameof(Queryable.Select) when AsLambda(call.Arguments[1]) is { Parameters.Count: 1 } projection:
                            return Substitution.Apply(projection, ElementOf(call.Arguments[0], projection.Parameters[0].Type));

                        case nameof(Queryable.Join) when call.Arguments.Count == 5 &&
                                                         AsLambda(call.Arguments[4]) is { Parameters.Count: 2 } result:
                            return Substitution.Apply(
                                result,
                                ElementOf(call.Arguments[0], result.Parameters[0].Type),
                                ElementOf(call.Arguments[1], result.Parameters[1].Type));

                        case nameof(Queryable.SelectMany) when AsLambda(call.Arguments[1]) is { Parameters.Count: 1 } collection:
                            var outer = ElementOf(call.Arguments[0], collection.Parameters[0].Type);

                            if (call.Arguments.Count == 3 && AsLambda(call.Arguments[2]) is { Parameters.Count: 2 } selector)
                            {
                                var inner = ElementOf(Substitution.Apply(collection, outer), selector.Parameters[1].Type);
                                return Substitution.Apply(selector, outer, inner);
                            }

                            return ElementOf(Substitution.Apply(collection, outer), elementType);
                    }
                }

                if ((call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable) ||
                     call.Method.DeclaringType?.Namespace == "Microsoft.EntityFrameworkCore") &&
                    SequenceElementType(call.Type) is { } produced &&
                    produced == SequenceElementType(call.Arguments[0].Type))
                {
                    // Where, OrderBy, Skip, Take, Distinct, IgnoreQueryFilters, AsNoTracking, TagWith, …: the
                    // element passes through unchanged.
                    return ElementOf(call.Arguments[0], elementType);
                }
            }

            return Expression.Parameter(elementType, "element");
        }
    }

    /// <summary>
    /// Replaces a lambda's parameters with expressions and simplifies member access on object construction,
    /// so <c>x =&gt; x.T</c> over <c>o =&gt; new { T = o.TenantId }</c> becomes <c>o.TenantId</c>.
    /// </summary>
    private sealed class Substitution(Dictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        public static Expression Apply(LambdaExpression lambda, params Expression[] arguments)
        {
            Dictionary<ParameterExpression, Expression> replacements = [];

            for (var i = 0; i < lambda.Parameters.Count; i++)
            {
                replacements[lambda.Parameters[i]] = arguments[i];
            }

            return new Substitution(replacements).Visit(lambda.Body);
        }

        // A replacement whose type does not fit (for example after a covariant cast of the query) becomes a
        // placeholder, which the guard treats as unresolved rather than building an invalid expression.
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement)
                ? node.Type.IsAssignableFrom(replacement.Type) ? replacement : Expression.Parameter(node.Type, "element")
                : node;

        protected override Expression VisitMember(MemberExpression node)
        {
            var instance = Visit(node.Expression);

            return instance is not null && Member(instance, node.Member.Name) is { } projected
                ? projected
                : node.Update(instance);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var visited = (MethodCallExpression)base.VisitMethodCall(node);

            // EF.Property(new { … }, "Name") reads the projected member.
            return IsEfProperty(visited) &&
                   ReadPropertyName(visited.Arguments[1]) is { } name &&
                   Member(visited.Arguments[0], name) is { } projected
                ? projected
                : visited;
        }

        private static Expression? Member(Expression instance, string name)
        {
            switch (StripConvert(instance))
            {
                case NewExpression { Members: { } members } created:
                    for (var i = 0; i < members.Count; i++)
                    {
                        if (members[i].Name == name)
                        {
                            return created.Arguments[i];
                        }
                    }

                    break;

                case MemberInitExpression initialised:
                    foreach (var binding in initialised.Bindings)
                    {
                        if (binding is MemberAssignment assignment && assignment.Member.Name == name)
                        {
                            return assignment.Expression;
                        }
                    }

                    break;
            }

            return null;
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
