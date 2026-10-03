using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenantry.EfCore;

// Extensions on EF Core's builders live in its namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Marks entity types in the model for Tenantry.
/// </summary>
public static class TenantryEntityTypeBuilderExtensions
{
    // What EF Core requires of an entity type (matches its own annotation on EntityTypeBuilder<TEntity>).
    private const DynamicallyAccessedMemberTypes EntityMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.Interfaces;

    /// <summary>
    /// Marks the entity type as one whose rows every tenant shares, as <see cref="SharedAcrossTenantsAttribute"/>
    /// does. It changes nothing in queries or saves.
    /// </summary>
    /// <param name="builder">The entity type's builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static EntityTypeBuilder IsSharedAcrossTenants(this EntityTypeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(TenantModel.SharedAcrossTenantsAnnotation, true);
    }

    /// <inheritdoc cref="IsSharedAcrossTenants(EntityTypeBuilder)"/>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    public static EntityTypeBuilder<TEntity> IsSharedAcrossTenants<
        [DynamicallyAccessedMembers(EntityMembers)] TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(TenantModel.SharedAcrossTenantsAnnotation, true);
    }
}
