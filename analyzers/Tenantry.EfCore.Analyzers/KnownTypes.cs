using Microsoft.CodeAnalysis;

namespace Tenantry.EfCore.Analyzers;

/// <summary>The Tenantry and EF Core types the analyzers look for, found once per compilation.</summary>
internal sealed class KnownTypes
{
    private KnownTypes(Compilation compilation)
    {
        TenantEntity = compilation.GetTypeByMetadataName("Tenantry.ITenantEntity`1");
        TenantDescriptor = compilation.GetTypeByMetadataName("Tenantry.ITenantDescriptor`1");
        SharedAcrossTenants = compilation.GetTypeByMetadataName("Tenantry.EfCore.SharedAcrossTenantsAttribute");
        DbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        DbSet = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        ModelBuilder = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.ModelBuilder");
        QueryableExtensions = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        RelationalDatabaseFacadeExtensions =
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions");
        TenantScopeFactory = compilation.GetTypeByMetadataName("Tenantry.ITenantScopeFactory`1");
        TenantContextSetter = compilation.GetTypeByMetadataName("Tenantry.ITenantContextSetter`1");
        EntityTypeBuilderExtensions =
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.TenantryEntityTypeBuilderExtensions");
        PrimaryKeyAttribute = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.PrimaryKeyAttribute");
        KeyAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.KeyAttribute");
        EntityTypeConfiguration = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
    }

    public INamedTypeSymbol? EntityTypeConfiguration { get; }

    public INamedTypeSymbol? EntityTypeBuilderExtensions { get; }

    public INamedTypeSymbol? PrimaryKeyAttribute { get; }

    public INamedTypeSymbol? KeyAttribute { get; }

    public INamedTypeSymbol? TenantEntity { get; }

    public INamedTypeSymbol? TenantDescriptor { get; }

    public INamedTypeSymbol? SharedAcrossTenants { get; }

    public INamedTypeSymbol? DbContext { get; }

    public INamedTypeSymbol? DbSet { get; }

    public INamedTypeSymbol? ModelBuilder { get; }

    public INamedTypeSymbol? QueryableExtensions { get; }

    public INamedTypeSymbol? RelationalDatabaseFacadeExtensions { get; }

    public INamedTypeSymbol? TenantScopeFactory { get; }

    public INamedTypeSymbol? TenantContextSetter { get; }

    public static KnownTypes For(Compilation compilation) => new(compilation);

    /// <summary>Whether <paramref name="type"/> implements the generic interface <paramref name="definition"/>.</summary>
    public static bool Implements(ITypeSymbol type, INamedTypeSymbol? definition) =>
        definition is not null &&
        (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition) ||
         type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition)));

    /// <summary>Whether <paramref name="type"/> derives from <paramref name="baseType"/>.</summary>
    public static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
            return false;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="method"/> is declared by <paramref name="type"/>, or by its generic definition.</summary>
    public static bool IsDeclaredBy(IMethodSymbol method, INamedTypeSymbol? type) =>
        type is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type);
}
