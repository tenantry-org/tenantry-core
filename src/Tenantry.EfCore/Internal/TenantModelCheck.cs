using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Checks, once per model, that tenant isolation is in place on every entity type that implements
/// <see cref="ITenantEntity{TKey}"/>, and that no other entity type shares their tables, and throws
/// <see cref="TenantIsolationViolationException"/> when it is not.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseTenantry()</c>'s model customizer gives each tenant-owned entity type the tenant query filter after all of
/// the context's own configuration, and makes its <c>TenantId</c> a concurrency token. Something that runs after it
/// could still undo that, such as a model-building convention, or a model it never built (a compiled model). The
/// tenant's queries would then return every tenant's rows. The query and save interceptors check the model they are
/// about to use, so such a model fails on its first query or save instead.
/// </para>
/// <para>
/// A filter passes when it is the tenant filter or contains it as one of its <c>&amp;&amp;</c> operands, as when it
/// was merged into the entity's own filter.
/// </para>
/// </remarks>
internal static class TenantModelCheck
{
    // Models that passed, with their isolation. Weak, so a model the application no longer uses is not kept alive.
    private static readonly ConditionalWeakTable<IModel, Checked> Passed = [];

    /// <summary>
    /// Throws when the context's model does not isolate a tenant-owned entity type, and otherwise returns the
    /// isolation for its tenant key type, or <see langword="null"/> when it has no tenant-owned entity types.
    /// </summary>
    public static TenantIsolation? Verify(DbContext context)
    {
        var model = context.Model;

        if (Passed.TryGetValue(model, out var passed))
        {
            return passed.Isolation;
        }

        var isolation = TenantIsolation.ForModel(model);

        if (isolation is not null)
        {
            foreach (var entityType in model.GetEntityTypes())
            {
                Verify(entityType, isolation.KeyType);
            }

            // Only a relational provider maps tables.
            if (context.Database.IsRelational())
            {
                TenantEntityTypes.ThrowIfATenantTableIsShared(model, isolation.KeyType);
            }
        }

        Passed.AddOrUpdate(model, new Checked(isolation));
        return isolation;
    }

    private static void Verify(IEntityType entityType, Type keyType)
    {
        var clrType = entityType.ClrType;

        if (!TenantEntityTypes.IsTenantEntity(clrType))
        {
            return;
        }

        if (entityType.IsOwned())
        {
            // EF Core reads an owned type's rows only through its owner and does not let it have a filter of its
            // own. The owner is itself checked as a tenant-owned entity type of this model.
            TenantEntityTypes.ThrowIfOwnerIsNotTenantEntity(entityType, keyType);
        }
        else if (!HasTenantFilter(entityType.GetRootType(), keyType))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                clrType.Name,
                $"Tenant-owned entity '{clrType.Name}' has no tenant query filter, so its queries could return every " +
                "tenant's rows. UseTenantry() adds it after OnModelCreating, so something replaced it later, such as a " +
                "model-building convention, or the model is one UseTenantry() did not build (a compiled model, which " +
                "it does not support).");
        }

        // Keyless entity types are never updated or deleted.
        if (entityType.FindPrimaryKey() is not null && !IsCheckedOnWrite(entityType))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                clrType.Name,
                $"The TenantId of tenant-owned entity '{clrType.Name}' is not a concurrency token, so UPDATE and " +
                "DELETE statements would not check the tenant a row is stored under. UseTenantry() makes it one; " +
                "do not configure TenantId with IsConcurrencyToken(false) in a convention.");
        }
    }

    // Whether UPDATE and DELETE statements match the stored TenantId: it is a concurrency token or part of the key.
    private static bool IsCheckedOnWrite(IEntityType entityType) =>
        entityType.FindProperty(TenantOwnership.TenantIdProperty) is { } tenantId &&
        (tenantId.IsConcurrencyToken || tenantId.IsPrimaryKey());

    private static bool HasTenantFilter(IEntityType rootType, Type keyType)
    {
#if EFCORE10_OR_GREATER
        return rootType.GetDeclaredQueryFilters().Any(filter => filter.Expression is { } lambda && IsTenantFilter(lambda, keyType));
#else
        return rootType.GetQueryFilter() is { } lambda && IsTenantFilter(lambda, keyType);
#endif
    }

    // `TenantFilter.HasTenant<TKey>(context) && entity.TenantId.Equals(TenantFilter.CurrentTenantId<TKey>(context))`,
    // either the whole filter or merged into an entity's own filter with &&.
    private static bool IsTenantFilter(LambdaExpression filter, Type keyType)
    {
        List<Expression> operands = [];
        CollectAndOperands(filter.Body, operands);

        return operands.Any(operand => TenantFilter.IsCall(operand, TenantFilter.HasTenantMethod, keyType)) &&
               operands.Any(operand => IsTenantMatch(operand, filter.Parameters[0], keyType));
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

    // entity.TenantId.Equals(TenantFilter.CurrentTenantId<TKey>(context))
    private static bool IsTenantMatch(Expression expression, ParameterExpression entity, Type keyType) =>
        StripConvert(expression) is MethodCallExpression { Object: { } instance, Arguments: [var argument] } call &&
        call.Method.Name == nameof(Equals) &&
        StripConvert(instance) is MemberExpression { Member.Name: TenantOwnership.TenantIdProperty, Expression: { } owner } &&
        StripConvert(owner) == entity &&
        TenantFilter.IsCall(StripConvert(argument), TenantFilter.CurrentTenantIdMethod, keyType);

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            expression = convert.Operand;
        }

        return expression;
    }

    private sealed record Checked(TenantIsolation? Isolation);
}

/// <summary>
/// Which <see cref="ITenantEntity{TKey}"/> interfaces an entity type implements.
/// </summary>
internal static class TenantEntityTypes
{
    public static bool IsTenantEntity([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type) =>
        KeyTypes(type).Any();

    /// <summary>
    /// The tenant key types <paramref name="type"/> implements <see cref="ITenantEntity{TKey}"/> with, including
    /// its own when it is that interface.
    /// </summary>
    public static IEnumerable<Type> KeyTypes([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type) =>
        type.GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsInterface && candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(ITenantEntity<>))
            .Select(candidate => candidate.GetGenericArguments()[0]);

    /// <summary>
    /// Throws when the root of <paramref name="entityType"/>'s inheritance hierarchy is not tenant-owned: EF Core
    /// filters a hierarchy only through its root, so nothing would isolate the tenant-owned derived type.
    /// </summary>
    public static void ThrowIfRootIsNotTenantEntity(IReadOnlyEntityType entityType, Type keyType)
    {
        var root = entityType.GetRootType();

        if (!KeyTypes(root.ClrType).Contains(keyType))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                entityType.ClrType.Name,
                $"Entity '{entityType.ClrType.Name}' is tenant-owned but its base entity type '{root.ClrType.Name}' is " +
                "not. EF Core applies query filters to the root of an inheritance hierarchy only, so implement " +
                $"ITenantEntity<{keyType.Name}> on '{root.ClrType.Name}'.");
        }
    }

    /// <summary>
    /// Throws when the owner of the tenant-owned owned type <paramref name="owned"/> (its first owner that is not
    /// itself owned) is not tenant-owned: EF Core reads owned rows only through their owner and filters only the
    /// owner, so nothing would keep one tenant from reading another's.
    /// </summary>
    public static void ThrowIfOwnerIsNotTenantEntity(IReadOnlyEntityType owned, Type keyType)
    {
        var owner = RootOwner(owned);

        if (!KeyTypes(owner.ClrType).Contains(keyType))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                owned.ClrType.Name,
                $"Owned entity '{owned.ClrType.Name}' is tenant-owned but its owner '{owner.ClrType.Name}' is not. " +
                "EF Core reads owned rows only through their owner and filters only the owner, so implement " +
                $"ITenantEntity<{keyType.Name}> on '{owner.ClrType.Name}'.");
        }
    }

    /// <summary>
    /// Throws when Tenantry cannot check the writes of owned type <paramref name="entityType"/> through its owner, under
    /// a tenant-owned owner: when it has no <c>TenantId</c> of its own and its key, or the key a type it owns is owned
    /// through, does not include its owner's key (an <c>UPDATE</c> or <c>DELETE</c> by its own key would match a row
    /// whatever owner it is stored under); or when it is owned by a tenant-owned type through a key that neither
    /// includes nor is part of that type's primary key, nor includes its <c>TenantId</c> (the owner Tenantry checks by
    /// its primary key need not be the row its foreign key names).
    /// </summary>
    public static void ThrowIfOwnershipIsUnchecked(IReadOnlyEntityType entityType, Type keyType)
    {
        if (entityType.FindOwnership() is not { } ownership || !KeyTypes(RootOwner(entityType).ClrType).Contains(keyType))
        {
            return;
        }

        var owner = ownership.PrincipalEntityType;

        if (IsTenantEntity(owner.ClrType) && !NamesTheCheckedRow(ownership.PrincipalKey, owner.FindPrimaryKey()))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                entityType.ClrType.Name,
                $"Owned entity '{entityType.ClrType.Name}' is owned through a key of '{owner.ClrType.Name}' that " +
                "neither includes nor is part of its primary key, nor includes its TenantId, so Tenantry cannot check " +
                $"that the owner its rows name is the current tenant's. Own it through the primary key of " +
                $"'{owner.ClrType.Name}', or through a key that includes its TenantId.");
        }

        if (IsTenantEntity(entityType.ClrType))
        {
            return;
        }

        bool KeyedByOwner(IReadOnlyKey? key) => key is not null && ownership.Properties.All(key.Properties.Contains);

        if (KeyedByOwner(entityType.FindPrimaryKey()) &&
            entityType.GetReferencingForeignKeys().Where(foreignKey => foreignKey.IsOwnership).All(owned => KeyedByOwner(owned.PrincipalKey)))
        {
            return;
        }

        throw new TenantIsolationViolationException(
            TenantIsolationViolationKind.ModelConfiguration,
            entityType.ClrType.Name,
            $"Owned entity '{entityType.ClrType.Name}' has no TenantId and a key that does not include its owner's key, " +
            "so an update or delete of one of its rows would match that row whichever tenant's owner it belongs to. " +
            "Keep its owner's key in its key (EF Core's default for OwnsMany), or implement " +
            $"ITenantEntity<{keyType.Name}> on it so its rows carry their tenant.");
    }

    /// <summary>
    /// Throws when an entity type that is neither owned nor tenant-owned is mapped to a table a tenant-owned one is
    /// (table splitting): it has no tenant filter or <c>TenantId</c>, so its queries would read, and its writes change,
    /// every tenant's rows of that table. Read from the finished relational model, which maps only real tables.
    /// </summary>
    public static void ThrowIfATenantTableIsShared(IModel model, Type keyType)
    {
        Dictionary<ITable, string> tenantTables = [];

        // A hierarchy whose root is not tenant-owned fails a check of its own (ThrowIfRootIsNotTenantEntity).
        foreach (var entityType in model.GetEntityTypes().Where(type => !type.IsOwned() && IsTenantEntity(type.GetRootType().ClrType)))
        {
            foreach (var mapping in entityType.GetTableMappings())
            {
                tenantTables.TryAdd(mapping.Table, entityType.ClrType.Name);
            }
        }

        foreach (var entityType in model.GetEntityTypes().Where(type => !type.IsOwned() && !IsTenantEntity(type.ClrType)))
        {
            foreach (var mapping in entityType.GetTableMappings())
            {
                if (tenantTables.TryGetValue(mapping.Table, out var tenantEntity))
                {
                    throw new TenantIsolationViolationException(
                        TenantIsolationViolationKind.ModelConfiguration,
                        entityType.ClrType.Name,
                        $"Entity '{entityType.ClrType.Name}' shares table '{mapping.Table.Name}' with tenant-owned " +
                        $"'{tenantEntity}' but is not tenant-owned, so its queries and writes of that table's rows are " +
                        $"not isolated. Implement ITenantEntity<{keyType.Name}> on it, or map it to a table of its own.");
                }
            }
        }
    }

    // Whether the row an ownership's principal key names is the one Tenantry checks, by the owner's primary key: the
    // key includes TenantId (the owned rows' foreign key then names the tenant), includes the primary key (which picks
    // the row), or is part of it (a key is unique, so the row it picks has that primary key or none does).
    private static bool NamesTheCheckedRow(IReadOnlyKey principalKey, IReadOnlyKey? primaryKey) =>
        principalKey.Properties.Any(property => property.Name == TenantOwnership.TenantIdProperty) ||
        (primaryKey is not null &&
         (primaryKey.Properties.All(principalKey.Properties.Contains) || principalKey.Properties.All(primaryKey.Properties.Contains)));

    // The first owner up an owned type's ownership chain that is not itself owned, as the root of its hierarchy.
    private static IReadOnlyEntityType RootOwner(IReadOnlyEntityType entityType)
    {
        while (entityType.IsOwned() && entityType.FindOwnership() is { } ownership)
        {
            entityType = ownership.PrincipalEntityType;
        }

        return entityType.GetRootType();
    }
}
