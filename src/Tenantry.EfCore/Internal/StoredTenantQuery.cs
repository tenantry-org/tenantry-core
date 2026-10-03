using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The query <see cref="TenantWriteGuard{TKey}"/> reads an entity's stored tenant with: its row by primary key, only if
/// that row is the current tenant's, with every query filter ignored.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's own read of a row (<c>GetDatabaseValues</c>) comes back through <see cref="DatabaseValuesQuery"/> with the
/// tenant filter, and on EF Core 8 and 9 with the entity's own filter too, which the tenant filter is merged into, so a
/// row of the current tenant's that the application's filter hides (a soft-deleted one) would read as missing. This
/// query names the tenant itself instead, as the tenant filter does, so no filter applies and another tenant's row is
/// never read. Writes ignore query filters too.
/// </para>
/// <para>
/// The tenant and the key values are captured, so EF Core sends them as parameters and compiles the query once per
/// entity type. It never has the shape of EF Core's database-values query, so <see cref="DatabaseValuesQuery"/> leaves
/// it alone. An owned type is read through its owner, as EF Core reads it.
/// </para>
/// </remarks>
internal static class StoredTenantQuery
{
    private static readonly MethodInfo Select = new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<object>>(
        Queryable.Select).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo SelectMany = new Func<IQueryable<object>, Expression<Func<object, IEnumerable<object>>>, IQueryable<object>>(
        Queryable.SelectMany).Method.GetGenericMethodDefinition();

    private static readonly FieldInfo CapturedValues = typeof(Captured).GetField(nameof(Captured.Values))!;

    /// <summary>
    /// The query for <paramref name="entry"/>'s stored <c>TenantId</c>, which returns it only when it is
    /// <paramref name="tenantId"/>, or <see langword="null"/> when a key value is <see langword="null"/>, so no row
    /// can match.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Builds a query EF Core compiles at run time, which requires dynamic code anyway.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "EF.Property and the Queryable methods have no trimming requirements on their type parameters.")]
    public static IQueryable<object?>? For<TKey>(DbContext context, EntityEntry entry, TKey tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var key = entry.Metadata.FindPrimaryKey()!.Properties;
        var values = new object?[key.Count];

        for (var i = 0; i < key.Count; i++)
        {
            if ((values[i] = entry.Property(key[i].Name).CurrentValue) is null)
            {
                return null;
            }
        }

        // As the tenant filter compares it. tenantId is captured, so it is a parameter.
        Expression<Func<object, bool>> ofTenant = e => EF.Property<TKey>(e, TenantOwnership.TenantIdProperty).Equals(tenantId);
        var entity = ofTenant.Parameters[0];
        var captured = Expression.Field(Expression.Constant(new Captured(values)), CapturedValues);
        var predicate = ofTenant.Body;

        // As EF Core's own key lookups compare each key property, and as parameters too.
        for (var i = 0; i < key.Count; i++)
        {
            var property = entity.CreateEFPropertyExpression(key[i]);
            var value = Expression.Convert(Expression.ArrayIndex(captured, Expression.Constant(i)), property.Type);
            predicate = Expression.AndAlso(predicate, Microsoft.EntityFrameworkCore.Infrastructure.ExpressionExtensions.CreateEqualsExpression(property, value));
        }

        return Root(context.GetService<IAsyncQueryProvider>(), entry.Metadata)
            .IgnoreQueryFilters()
            .Where(Expression.Lambda<Func<object, bool>>(predicate, entity))
            .Select(e => (object?)EF.Property<TKey>(e, TenantOwnership.TenantIdProperty));
    }

    // The entity type's rows: an owned type's through its owner, by the navigation from the owner to it.
    [RequiresDynamicCode("Builds a query for an entity type known only at run time.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The Queryable methods have no trimming requirements on their type parameters.")]
    private static IQueryable<object> Root(IAsyncQueryProvider provider, IEntityType entityType)
    {
        if (entityType.FindOwnership() is not { PrincipalToDependent: { } navigation } ownership)
        {
            return provider.CreateQuery<object>(new EntityQueryRootExpression(provider, entityType));
        }

        var owners = Root(provider, ownership.PrincipalEntityType);
        var owner = Expression.Parameter(typeof(object), "owner");
        var owned = owner.CreateEFPropertyExpression(navigation, makeNullable: false);

        var call = navigation.IsCollection
            ? Expression.Call(
                SelectMany.MakeGenericMethod(typeof(object), entityType.ClrType),
                owners.Expression,
                Expression.Quote(Expression.Lambda(
                    typeof(Func<,>).MakeGenericType(typeof(object), typeof(IEnumerable<>).MakeGenericType(entityType.ClrType)), owned, owner)))
            : Expression.Call(
                Select.MakeGenericMethod(typeof(object), entityType.ClrType),
                owners.Expression,
                Expression.Quote(Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(object), entityType.ClrType), owned, owner)));

        return provider.CreateQuery<object>(call);
    }

    private sealed class Captured(object?[] values)
    {
        public readonly object?[] Values = values;
    }
}
