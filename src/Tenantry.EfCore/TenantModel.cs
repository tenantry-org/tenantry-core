using System.ComponentModel;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore;

/// <summary>
/// Reads which entity types of an EF Core model Tenantry isolates, for packages and tests that build on it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class TenantModel
{
    /// <summary>The model annotation <c>IsSharedAcrossTenants()</c> sets.</summary>
    public const string SharedAcrossTenantsAnnotation = "Tenantry:SharedAcrossTenants";

    /// <summary>
    /// Returns whether <paramref name="model"/> has an entity type that implements <see cref="ITenantEntity{TKey}"/>,
    /// which is what <c>UseTenantry()</c> isolates. A model without one has nothing for Tenantry's filters and
    /// checks to do.
    /// </summary>
    /// <param name="model">The model, such as <c>context.Model</c>.</param>
    public static bool HasTenantOwnedEntityTypes(IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetEntityTypes().Any(IsTenantOwned);
    }

    /// <summary>
    /// Returns whether <paramref name="entityType"/> is tenant-owned: it implements <see cref="ITenantEntity{TKey}"/>,
    /// or it is an owned type whose owner is.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    public static bool IsTenantOwned(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (IReadOnlyEntityType? current = entityType; current is not null; current = current.FindOwnership()?.PrincipalEntityType)
        {
            if (TenantEntityTypes.IsTenantEntity(current.ClrType))
            {
                return true;
            }

            if (!current.IsOwned())
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="entityType"/> is marked as shared by every tenant, with
    /// <see cref="SharedAcrossTenantsAttribute"/> or <c>IsSharedAcrossTenants()</c>, or belongs to a type that is: a
    /// base type of its hierarchy, or the type that owns it.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    public static bool IsSharedAcrossTenants(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (IReadOnlyEntityType? current = entityType; current is not null; current = current.FindOwnership()?.PrincipalEntityType)
        {
            for (IReadOnlyEntityType? type = current; type is not null; type = type.BaseType)
            {
                if (IsMarked(type))
                {
                    return true;
                }
            }

            if (!current.IsOwned())
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the entity types of <paramref name="model"/> that are neither tenant-owned nor marked as shared by
    /// every tenant. In a database that tenants share, Tenantry does not keep their rows apart.
    /// </summary>
    /// <param name="model">The model, such as <c>context.Model</c>.</param>
    /// <remarks>
    /// It returns the types to mark, which are the roots of inheritance hierarchies. A derived type, an owned type and
    /// the join entity type of a many-to-many relationship that holds only the two foreign keys are left out: each
    /// follows the type it belongs to, its hierarchy's root, its owner or the entity types it joins. A join entity type
    /// with other properties or foreign keys is returned like any other. <c>UseTenantry()</c> checks a model that has a
    /// tenant-owned entity type against this list, as <see cref="EfCoreIsolationOptions.OnUnclassifiedEntityType"/>
    /// says.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyEntityType> FindUnisolatedEntityTypes(IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return
        [
            .. model.GetEntityTypes().Where(entityType =>
                entityType.BaseType is null &&
                !entityType.IsOwned() &&
                !IsJoinEntityType(entityType) &&
                !IsTenantOwned(entityType) &&
                !IsSharedAcrossTenants(entityType)),
        ];
    }

    // The join entity type of a many-to-many relationship that holds nothing but the join: its foreign keys are the two
    // its skip navigations go through, and every property is part of one of them. A join with data of its own, or
    // with another foreign key, is an entity type like any other.
    private static bool IsJoinEntityType(IReadOnlyEntityType entityType)
    {
        var foreignKeys = entityType.GetForeignKeys().ToList();

        return foreignKeys.Count == 2 &&
               foreignKeys.All(foreignKey => foreignKey.GetReferencingSkipNavigations().Any()) &&
               entityType.GetProperties().All(property => foreignKeys.Any(foreignKey => foreignKey.Properties.Contains(property)));
    }

    internal static bool IsMarked(IReadOnlyEntityType entityType) =>
        entityType.FindAnnotation(SharedAcrossTenantsAnnotation)?.Value is true ||
        HasAttribute(entityType.ClrType);

    private static bool HasAttribute(Type type) =>
        type.GetCustomAttribute<SharedAcrossTenantsAttribute>(inherit: true) is not null;
}
