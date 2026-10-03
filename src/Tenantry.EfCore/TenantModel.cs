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
    /// Returns whether <paramref name="entityType"/>, or the type that owns it, is marked as shared by every tenant,
    /// with <see cref="SharedAcrossTenantsAttribute"/> or <c>IsSharedAcrossTenants()</c>.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    public static bool IsSharedAcrossTenants(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (IReadOnlyEntityType? current = entityType; current is not null; current = current.FindOwnership()?.PrincipalEntityType)
        {
            if (IsMarked(current))
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
    /// Returns the entity types of <paramref name="model"/> that are neither tenant-owned nor marked as shared by
    /// every tenant. In a database that tenants share, Tenantry does not keep their rows apart.
    /// </summary>
    /// <param name="model">The model, such as <c>context.Model</c>.</param>
    public static IReadOnlyList<IReadOnlyEntityType> FindUnisolatedEntityTypes(IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return [.. model.GetEntityTypes().Where(entityType => !IsTenantOwned(entityType) && !IsSharedAcrossTenants(entityType))];
    }

    internal static bool IsMarked(IReadOnlyEntityType entityType) =>
        entityType.FindAnnotation(SharedAcrossTenantsAnnotation)?.Value is true ||
        HasAttribute(entityType.ClrType);

    private static bool HasAttribute(Type type) =>
        type.GetCustomAttribute<SharedAcrossTenantsAttribute>(inherit: true) is not null;
}
