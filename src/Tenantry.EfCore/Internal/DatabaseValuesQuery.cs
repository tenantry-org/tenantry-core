using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Keeps the tenant filter on the query EF Core runs for <c>Entry(…).Reload()</c> and <c>GetDatabaseValues()</c>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core reads an entity's database values by its key with <c>IgnoreQueryFilters()</c>, so an entity attached with
/// another tenant's key (a forged write, which then fails with <c>DbUpdateConcurrencyException</c>) would get that
/// tenant's values. When EF Core compiles that query for a tenant-owned entity type, or an entity type it owns,
/// <see cref="KeepTenantFilter"/> takes <c>IgnoreQueryFilters()</c> out, so the row is read only if it belongs to the
/// current tenant: another tenant's row reads as deleted. On EF Core 10 and later it ignores the model's other named
/// filters in its place, as EF Core meant to (an unnamed one is named <see cref="TenantryQueryFilters.Application"/>);
/// on EF Core 8 and 9, where Tenantry merges the tenant filter into the entity's own, that filter applies as well.
/// </para>
/// <para>
/// EF Core does not document the query. It builds
/// <c>source.AsNoTracking().IgnoreQueryFilters().Where(e =&gt; key).Select(e =&gt; new object[] { … })</c> over
/// <c>IQueryable&lt;object&gt;</c>, in every supported version, and <c>DatabaseValuesQueryShapeTests</c> pins that
/// shape, so a version that changes it fails a focused test. A query of the application's own is changed only if it
/// has that shape and reads a tenant-owned entity type, and then it only regains the tenant filter.
/// </para>
/// </remarks>
internal static class DatabaseValuesQuery
{
    /// <summary>Returns <paramref name="query"/> with the tenant filter kept on EF Core's database-values queries.</summary>
    public static Expression KeepTenantFilter(Expression query, IModel model, TenantIsolation isolation) =>
        new Rewriter(model, isolation).Visit(query);

    /// <summary>
    /// The <c>IgnoreQueryFilters()</c> call of an EF Core database-values query that reads a tenant-owned entity type,
    /// or <see langword="null"/> when <paramref name="node"/> is not one.
    /// </summary>
    internal static MethodCallExpression? FindIgnoreQueryFilters(MethodCallExpression node, TenantIsolation isolation)
    {
        // Select<object, object[]>(Where<object>(IgnoreQueryFilters<T>(source), e => key), e => new object[] { … })
        if (node.Method is not { Name: nameof(Queryable.Select), IsGenericMethod: true } select ||
            select.DeclaringType != typeof(Queryable) ||
            select.GetGenericArguments() is not [var element, var result] ||
            element != typeof(object) ||
            result != typeof(object[]) ||
            ExecuteUpdateSetterReader.AsLambda(node.Arguments[1]) is not { Body: NewArrayExpression { NodeType: ExpressionType.NewArrayInit } })
        {
            return null;
        }

        return node.Arguments[0] is MethodCallExpression { Method.Name: nameof(Queryable.Where) } where &&
               where.Method.DeclaringType == typeof(Queryable) &&
               where.Arguments[0] is MethodCallExpression { Arguments: [var source] } ignore &&
               ignore.Method is { Name: nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters) } &&
               ignore.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions) &&
               ReadsTenantEntity(source, isolation)
            ? ignore
            : null;
    }

    // An owned entity type's values are read through its owner's query root, so the owner's filter covers it.
    private static bool ReadsTenantEntity(Expression source, TenantIsolation isolation)
    {
        TenantRootFinder finder = new(isolation);
        finder.Visit(source);
        return finder.Found;
    }

    private sealed class Rewriter(IModel model, TenantIsolation isolation) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (FindIgnoreQueryFilters(node, isolation) is not { } ignore)
            {
                return base.VisitMethodCall(node);
            }

            var where = (MethodCallExpression)node.Arguments[0];

            return node.Update(null, [where.Update(null, [KeepFilters(ignore), where.Arguments[1]]), node.Arguments[1]]);
        }

#if EFCORE10_OR_GREATER
        // EF Core 10 names filters, and Tenantry names an unnamed one, so the others can still be ignored by name.
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Runs while EF Core compiles a query, which requires dynamic code anyway.")]
        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "IgnoreQueryFilters has no trimming requirements on its type parameter.")]
        private Expression KeepFilters(MethodCallExpression ignore)
        {
            var source = ignore.Arguments[0];
            var others = model.GetEntityTypes()
                .SelectMany(entityType => entityType.GetDeclaredQueryFilters())
                .Select(filter => filter.Key)
                .OfType<string>()
                .Where(key => key != TenantryQueryFilters.Tenant)
                .Distinct()
                .ToArray();

            if (others.Length == 0)
            {
                return source;
            }

            var ignoreNamed = new Func<IQueryable<object>, IReadOnlyCollection<string>, IQueryable<object>>(
                EntityFrameworkQueryableExtensions.IgnoreQueryFilters).Method.GetGenericMethodDefinition();

            return Expression.Call(
                ignoreNamed.MakeGenericMethod(ignore.Method.GetGenericArguments()),
                source,
                Expression.Constant(others, typeof(IReadOnlyCollection<string>)));
        }
#else
        // EF Core 8 and 9 have one filter per entity type, so the tenant filter comes with the entity's own.
        private Expression KeepFilters(MethodCallExpression ignore)
        {
            _ = model;
            return ignore.Arguments[0];
        }
#endif
    }

    private sealed class TenantRootFinder(TenantIsolation isolation) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root && isolation.IsTenantEntity(root.EntityType.ClrType))
            {
                Found = true;
            }

            return base.VisitExtension(node);
        }
    }
}
