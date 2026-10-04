using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenantry.EfCore;
using Tenantry.EfCore.Internal;

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
    [RequiresUnreferencedCode(EfCoreRequirements.UnreferencedCode)]
    [RequiresDynamicCode(EfCoreRequirements.DynamicCode)]
    public static EntityTypeBuilder IsSharedAcrossTenants(this EntityTypeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(TenantModel.SharedAcrossTenantsAnnotation, true);
    }

    /// <inheritdoc cref="IsSharedAcrossTenants(EntityTypeBuilder)"/>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    [RequiresUnreferencedCode(EfCoreRequirements.UnreferencedCode)]
    [RequiresDynamicCode(EfCoreRequirements.DynamicCode)]
    public static EntityTypeBuilder<TEntity> IsSharedAcrossTenants<
        [DynamicallyAccessedMembers(EntityMembers)] TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(TenantModel.SharedAcrossTenantsAnnotation, true);
    }
}
