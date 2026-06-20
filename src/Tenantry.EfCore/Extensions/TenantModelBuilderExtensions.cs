using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tenantry.Core;

namespace Tenantry.EfCore.Extensions;

/// <summary>
/// Extension methods for <see cref="ModelBuilder"/> that apply tenant isolation
/// to all entities implementing <see cref="ITenantScoped{TKey}"/>.
/// </summary>
public static class TenantModelBuilderExtensions
{
#if NET10_0_OR_GREATER 
    private const string TenantryFilterKey = "__TenantryFilter__";
#endif
    
    /// <summary>
    /// Discovers all entity types in the model that implement <see cref="ITenantScoped{TKey}"/>
    /// and applies a global query filter that restricts results to the current tenant.
    /// Also configures an index on the <c>TenantId</c> column for query performance.
    /// </summary>
    /// <typeparam name="TKey">
    /// The tenant identifier type. Must match the key type used in
    /// <see cref="ITenantScoped{TKey}"/> and the Tenantry DI registration.
    /// </typeparam>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> subtype that also implements <see cref="ITenantAwareDbContext{TKey}"/>.
    /// </typeparam>
    /// <param name="modelBuilder">The model builder from <c>OnModelCreating</c>.</param>
    /// <param name="context">
    /// The calling <c>DbContext</c> instance. Pass <c>this</c> from inside
    /// <c>OnModelCreating</c>:
    /// <code>
    /// modelBuilder.ApplyTenantFilters{Guid, AppDbContext}(this);
    /// </code>
    /// </param>
    /// <remarks>
    /// <para>
    /// EF Core caches the compiled query plan but re-evaluates <c>DbContext</c>
    /// property accesses on every execution. By closing the filter over the
    /// <c>DbContext</c> (rather than an external <c>ITenantContext&lt;TKey&gt;</c> service),
    /// the correct tenant ID is always used even though the model is built once
    /// and shared across context instances.
    /// </para>
    /// <para>
    /// Implement <see cref="ITenantAwareDbContext{TKey}"/> on your <c>DbContext</c> and
    /// expose <c>CurrentTenantId</c> as a property that delegates to your injected
    /// <c>ITenantContext&lt;TKey&gt;</c>. This method is idempotent and combines the tenant
    /// filter with any existing query filters.
    /// </para>
    /// </remarks>
    [RequiresDynamicCode("Expression tree construction requires dynamic code generation.")]
    [RequiresUnreferencedCode("Iterates model entity types and accesses members by name.")]
    public static void ApplyTenantFilters<TKey, TContext>(
        this ModelBuilder modelBuilder,
        TContext context)
        where TKey : IEquatable<TKey>, IParsable<TKey>
        where TContext : DbContext, ITenantAwareDbContext<TKey>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped<TKey>).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var builder = modelBuilder.Entity(entityType.ClrType);
            var tenantFilter = BuildFilterExpression<TKey, TContext>(entityType.ClrType, context);

#if NET10_0_OR_GREATER
            // In EF Core 10+, keyed filters are supported, but not in combination with unkeyed
            var existingFilters = entityType.GetDeclaredQueryFilters();

            // If nothing registered we can add keyed and exit
            if (existingFilters.Count == 0)
            {
                builder.HasQueryFilter(TenantryFilterKey, tenantFilter);
                continue;
            }
            
            // Check if we've already applied our filter, can exit
            if (existingFilters.Any(f => f.Key == TenantryFilterKey))
            {
                continue;
            }

            // We need to check if unkeyed filters are already applied, in which case we have to merge them
            if (existingFilters.Any(f => f.Key == null))
            {
                tenantFilter = existingFilters
                    .Where(f => f.Expression != null)
                    .Aggregate(tenantFilter, (current, existingFilter) => 
                        CombineFilters(existingFilter.Expression!, current));

                builder.HasQueryFilter(tenantFilter);
                continue;
            }
            
            // Otherwise, we can safely add the keyed filter
            builder.HasQueryFilter(TenantryFilterKey, tenantFilter);
#else
            // Pre-EF10: combine with existing filter since keyed filters aren't supported
            var existingFilter = entityType.GetQueryFilter();

            if (existingFilter != null)
            {
                tenantFilter = CombineFilters(existingFilter, tenantFilter);
            }

            builder.HasQueryFilter(tenantFilter);
#endif
        }
    }

    [RequiresDynamicCode("Expression tree construction requires dynamic code generation.")]
    private static LambdaExpression CombineFilters(LambdaExpression existing, LambdaExpression tenant)
    {
        // Unify the parameter to the one used in the 'existing' filter to avoid parameter mismatch
        var newTenantBody = ParameterReplaceVisitor.Replace(
            tenant.Body,
            tenant.Parameters[0],
            existing.Parameters[0]);

        var combinedBody = Expression.AndAlso(existing.Body, newTenantBody);
        return Expression.Lambda(combinedBody, existing.Parameters);
    }

    [RequiresDynamicCode("Expression tree construction requires dynamic code generation.")]
    [RequiresUnreferencedCode("Accesses entity members by name via Expression.Property.")]
    private static LambdaExpression BuildFilterExpression<TKey, TContext>(
        Type entityClrType,
        TContext context)
        where TKey : IEquatable<TKey>, IParsable<TKey>
        where TContext : DbContext, ITenantAwareDbContext<TKey>
    {
        var entityParameter = Expression.Parameter(entityClrType, "entity");
        var tenantIdAccess = Expression.Property(entityParameter, nameof(ITenantScoped<>.TenantId));
        var template = CreateFilterTemplate<TKey, TContext>();

        var body = ParameterReplaceVisitor.Replace(
            template.Body,
            template.Parameters[0],
            Expression.Constant(context));

        body = ParameterReplaceVisitor.Replace(
            body,
            template.Parameters[1],
            tenantIdAccess);

        return Expression.Lambda(body, entityParameter);
    }

    private static Expression<Func<TContext, TKey, bool>> CreateFilterTemplate<TKey, TContext>()
        where TKey : IEquatable<TKey>, IParsable<TKey>
        where TContext : DbContext, ITenantAwareDbContext<TKey>
    {
        // Close the filter over the DbContext instance ('context'), not an external service.
        // EF Core recognises DbContext captures and re-evaluates the property access
        // (context.CurrentTenantId) against the *current* executing DbContext on every
        // query, even though the compiled plan is cached. External service captures are
        // evaluated once at plan-compile time and baked in as constants.
        return (dbContext, tenantId) =>
            !Equals(dbContext.CurrentTenantId, default(TKey)) &&
            tenantId.Equals(dbContext.CurrentTenantId);
    }

    private sealed class ParameterReplaceVisitor(ParameterExpression source, Expression replacement)
        : ExpressionVisitor
    {
        public static Expression Replace(Expression expression, ParameterExpression source, Expression replacement)
        {
            return new ParameterReplaceVisitor(source, replacement).Visit(expression);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == source 
                ? replacement 
                : base.VisitParameter(node);
        }
    }
}
