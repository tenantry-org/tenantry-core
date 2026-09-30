using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// What the tenant query filter calls to read the current tenant:
/// <c>TenantFilter.HasTenant&lt;TKey&gt;(context) &amp;&amp; entity.TenantId.Equals(TenantFilter.CurrentTenantId&lt;TKey&gt;(context))</c>.
/// </summary>
/// <remarks>
/// The filter passes a <see cref="DbContext"/> constant, which EF Core replaces with the context running the query, and
/// it evaluates both calls as query parameters on every execution. So a model built once serves every tenant, a
/// pooled context serves whichever tenant is current, and no tenant id is ever reserved to mean "none".
/// </remarks>
internal static class TenantFilter
{
    public static readonly MethodInfo HasTenantMethod = typeof(TenantFilter).GetMethod(nameof(HasTenant))!;

    public static readonly MethodInfo CurrentTenantIdMethod = typeof(TenantFilter).GetMethod(nameof(CurrentTenantId))!;

    public static bool HasTenant<TKey>(DbContext context)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        ApplicationServices.TenantContext<TKey>(context).HasTenant;

    public static TKey? CurrentTenantId<TKey>(DbContext context)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        ApplicationServices.TenantContext<TKey>(context).CurrentTenantId;

    /// <summary>Whether <paramref name="expression"/> calls <paramref name="method"/> for <paramref name="keyType"/>.</summary>
    public static bool IsCall(Expression expression, MethodInfo method, Type keyType) =>
        expression is MethodCallExpression { Method: { IsGenericMethod: true } called } &&
        called.GetGenericMethodDefinition() == method &&
        called.GetGenericArguments()[0] == keyType;
}
