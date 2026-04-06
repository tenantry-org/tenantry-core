using Tenantry.Core;

namespace Tenantry.EfCore;

/// <summary>
/// Marks a <see cref="Microsoft.EntityFrameworkCore.DbContext"/> as tenant-aware,
/// exposing the current tenant identifier for use in EF Core global query filters.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <para>
/// EF Core compiles global query filters once and caches the compiled plan.
/// For the filter to reflect the correct tenant on every query, the expression
/// must close over the <c>DbContext</c> itself — EF Core re-evaluates
/// <c>DbContext</c> property accesses at query-execution time, whereas external
/// service captures (e.g. a captured <c>ITenantContext&lt;TKey&gt;</c> injected
/// into the constructor) are evaluated once at plan-compile time and baked in as
/// constants. This behaviour applies specifically to global query filters; inline
/// <c>.Where()</c> clauses do re-read per execution.
/// </para>
/// <para>
/// Implement this interface on your <c>DbContext</c>, expose
/// <c>CurrentTenantId</c> as a property that delegates to your injected
/// <c>ITenantContext&lt;TKey&gt;</c>, then call
/// <c>modelBuilder.ApplyTenantFilters&lt;TKey, TContext&gt;(this)</c> inside
/// <c>OnModelCreating</c>.
/// </para>
/// </remarks>
public interface ITenantAwareDbContext<out TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// The identifier of the tenant currently in scope for this context,
    /// or <c>null</c> if no tenant has been resolved.
    /// </summary>
    TKey? CurrentTenantId { get; }
}
