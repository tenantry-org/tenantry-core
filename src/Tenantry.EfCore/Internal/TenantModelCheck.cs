using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Checks, once per model, that tenant isolation is in place on every entity type that implements
/// <see cref="ITenantScoped{TKey}"/>, and throws <see cref="TenantIsolationViolationException"/> when it is not.
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyTenantFilters</c> gives each tenant-scoped entity type the tenant query filter and makes its
/// <c>TenantId</c> a concurrency token, but configuration that runs after it can undo that: before EF Core 10 a
/// later <c>HasQueryFilter</c> replaces the entity's only filter, and an entity type added later gets no filter at
/// all. The tenant's queries would then return every tenant's rows. The query and save interceptors check the model
/// they are about to use, so such a model fails on its first query or save instead.
/// </para>
/// <para>
/// A filter passes when it is the tenant filter or, when <c>ApplyTenantFilters</c> merged it with the entity's own
/// filter, contains it as one of its <c>&amp;&amp;</c> operands. Entities that implement
/// <see cref="ITenantScoped{TKey}"/> with another key type fail too: nothing isolates them.
/// </para>
/// </remarks>
internal static class TenantModelCheck<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // Models that passed. Weak, so a model the application no longer uses is not kept alive by the check.
    private static readonly ConditionalWeakTable<IModel, object> Passed = new();

    /// <summary>Throws when the context's model does not isolate a tenant-scoped entity type.</summary>
    public static void Verify(DbContext context)
    {
        var model = context.Model;

        if (Passed.TryGetValue(model, out _))
        {
            return;
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            Verify(entityType, context.GetType());
        }

        Passed.AddOrUpdate(model, Passed);
    }

    private static void Verify(IEntityType entityType, Type contextType)
    {
        var clrType = entityType.ClrType;

        if (!TenantScopedTypes.IsTenantScoped(clrType))
        {
            return;
        }

        TenantScopedTypes.ThrowIfOtherKeyType<TKey>(clrType);

        if (entityType.IsOwned())
        {
            // EF Core reads an owned type's rows only through its owner and does not let it have a filter of its
            // own. The owner is itself checked as a tenant-scoped entity type of this model.
            TenantScopedTypes.ThrowIfOwnerIsNotTenantScoped<TKey>(entityType);
        }
        else if (!HasTenantFilter(entityType.GetRootType()))
        {
            throw new TenantIsolationViolationException(
                clrType.Name,
                $"Tenant-scoped entity '{clrType.Name}' has no tenant query filter that Tenantry recognises, so its " +
                "queries could return every tenant's rows. Only the filter ApplyTenantFilters adds counts: call " +
                $"modelBuilder.ApplyTenantFilters<{typeof(TKey).Name}, {contextType.Name}>(this) (or base.OnModelCreating " +
                "in a MultiTenantDbContext) at the end of OnModelCreating, after your own configuration. An entity type " +
                "added after it gets no tenant filter, a HasQueryFilter call after it can replace the tenant filter, and " +
                "a filter written by hand or set by a convention or model customizer after OnModelCreating is not " +
                "recognised.");
        }

        // Keyless entity types are never updated or deleted.
        if (entityType.FindPrimaryKey() is not null && !IsCheckedOnWrite(entityType))
        {
            throw new TenantIsolationViolationException(
                clrType.Name,
                $"The TenantId of tenant-scoped entity '{clrType.Name}' is not a concurrency token, so UPDATE and " +
                "DELETE statements would not check the tenant a row is stored under. ApplyTenantFilters makes it one; " +
                "do not configure TenantId with IsConcurrencyToken(false).");
        }
    }

    // Whether UPDATE and DELETE statements match the stored TenantId: it is a concurrency token or part of the key.
    private static bool IsCheckedOnWrite(IEntityType entityType) =>
        entityType.FindProperty(nameof(ITenantScoped<>.TenantId)) is { } tenantId &&
        (tenantId.IsConcurrencyToken || tenantId.IsPrimaryKey());

    private static bool HasTenantFilter(IEntityType rootType)
    {
#if NET10_0_OR_GREATER
        return rootType.GetDeclaredQueryFilters().Any(filter => filter.Expression is { } lambda && IsTenantFilter(lambda));
#else
        return rootType.GetQueryFilter() is { } lambda && IsTenantFilter(lambda);
#endif
    }

    // ApplyTenantFilters' predicate is `!Equals(context.CurrentTenantId, default) && entity.TenantId.Equals(context.CurrentTenantId)`,
    // either the whole filter or merged into an entity's own filter with &&.
    private static bool IsTenantFilter(LambdaExpression filter)
    {
        List<Expression> operands = [];
        CollectAndOperands(filter.Body, operands);

        return operands.Any(IsHasTenantCheck) && operands.Any(operand => IsTenantMatch(operand, filter.Parameters[0]));
    }

    private static void CollectAndOperands(Expression expression, List<Expression> operands)
    {
        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso } and)
        {
            CollectAndOperands(and.Left, operands);
            CollectAndOperands(and.Right, operands);
        }
        else
        {
            operands.Add(expression);
        }
    }

    // !Equals(context.CurrentTenantId, default)
    private static bool IsHasTenantCheck(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression { Object: null } call } &&
        call.Method.Name == nameof(Equals) &&
        call.Arguments.Count == 2 &&
        IsCurrentTenantId(call.Arguments[0]);

    // entity.TenantId.Equals(context.CurrentTenantId)
    private static bool IsTenantMatch(Expression expression, ParameterExpression entity) =>
        StripConvert(expression) is MethodCallExpression { Object: { } instance } call &&
        call.Method.Name == nameof(Equals) &&
        call.Arguments.Count == 1 &&
        StripConvert(instance) is MemberExpression { Member.Name: nameof(ITenantScoped<>.TenantId), Expression: { } owner } &&
        StripConvert(owner) == entity &&
        IsCurrentTenantId(call.Arguments[0]);

    private static bool IsCurrentTenantId(Expression expression) =>
        StripConvert(expression) is MemberExpression { Member: PropertyInfo { Name: nameof(ITenantAwareDbContext<>.CurrentTenantId) } property } &&
        typeof(ITenantAwareDbContext<TKey>).IsAssignableFrom(property.DeclaringType);

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            expression = convert.Operand;
        }

        return expression;
    }
}

/// <summary>
/// Which <see cref="ITenantScoped{TKey}"/> interfaces an entity type implements.
/// </summary>
internal static class TenantScopedTypes
{
    public static bool IsTenantScoped([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type) => KeyTypes(type).Any();

    /// <summary>
    /// Throws when <paramref name="type"/> implements <see cref="ITenantScoped{TKey}"/> with a key type other than
    /// <typeparamref name="TKey"/>, the tenant key type in use, because such an entity gets no tenant filter and no
    /// write isolation.
    /// </summary>
    public static void ThrowIfOtherKeyType<TKey>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (KeyTypes(type).FirstOrDefault(key => key != typeof(TKey)) is { } other)
        {
            throw new TenantIsolationViolationException(
                type.Name,
                $"Entity '{type.Name}' implements ITenantScoped<{other.Name}>, but the tenant key type here is " +
                $"{typeof(TKey).Name}, so nothing would isolate it. Use one tenant key type for AddTenantry, " +
                "ApplyTenantFilters and ITenantScoped.");
        }
    }

    /// <summary>
    /// Throws when the owner of the tenant-scoped owned type <paramref name="owned"/> (its first owner that is not
    /// itself owned) is not tenant-scoped: EF Core reads owned rows only through their owner and filters only the
    /// owner, so nothing would keep one tenant from reading another's.
    /// </summary>
    public static void ThrowIfOwnerIsNotTenantScoped<TKey>(IReadOnlyEntityType owned)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var owner = owned;

        while (owner.IsOwned() && owner.FindOwnership() is { } ownership)
        {
            owner = ownership.PrincipalEntityType;
        }

        owner = owner.GetRootType();

        if (!typeof(ITenantScoped<TKey>).IsAssignableFrom(owner.ClrType))
        {
            throw new TenantIsolationViolationException(
                owned.ClrType.Name,
                $"Owned entity '{owned.ClrType.Name}' is tenant-scoped but its owner '{owner.ClrType.Name}' is not. " +
                "EF Core reads owned rows only through their owner and filters only the owner, so implement " +
                $"ITenantScoped<{typeof(TKey).Name}> on '{owner.ClrType.Name}'.");
        }
    }

    private static IEnumerable<Type> KeyTypes([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type) =>
        type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(ITenantScoped<>))
            .Select(candidate => candidate.GetGenericArguments()[0]);
}
