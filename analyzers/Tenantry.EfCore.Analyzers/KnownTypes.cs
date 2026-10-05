using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

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
        Queryable = compilation.GetTypeByMetadataName("System.Linq.IQueryable");
        Expression = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        DbContextOptionsBuilder = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder");
        ServiceCollectionExtensions =
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions");
        DbContextOptionsConfiguration =
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration`1");
    }

    /// <summary>EF Core 9 and later, where every registration's options of a context apply, not only the first's.</summary>
    public INamedTypeSymbol? DbContextOptionsConfiguration { get; }

    public INamedTypeSymbol? DbContextOptionsBuilder { get; }

    public INamedTypeSymbol? ServiceCollectionExtensions { get; }

    public INamedTypeSymbol? Queryable { get; }

    public INamedTypeSymbol? Expression { get; }

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

    /// <summary>
    /// Whether <paramref name="type"/> implements the generic interface <paramref name="definition"/>; a type parameter
    /// does when one of its constraints does.
    /// </summary>
    public static bool Implements(ITypeSymbol type, INamedTypeSymbol? definition) =>
        definition is not null &&
        (type is ITypeParameterSymbol parameter
            ? parameter.ConstraintTypes.Any(constraint => Implements(constraint, definition))
            : SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition) ||
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

    /// <summary>The operation inside the implicit conversions around it.</summary>
    public static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;

        return operation;
    }

    /// <summary>The <c>DbSet&lt;T&gt;</c> properties a type declares, with the entity type of each.</summary>
    public IEnumerable<(IPropertySymbol Property, ITypeSymbol Entity)> DbSets(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(property => (property, Entity: DbSetEntity(property)))
            .Where(set => set.Entity is not null)
            .Select(set => (set.property, set.Entity!));

    /// <summary>The entity type of a <c>DbSet&lt;T&gt;</c> property; none for another property.</summary>
    public ITypeSymbol? DbSetEntity(IPropertySymbol property) =>
        property.Type is INamedTypeSymbol { IsGenericType: true } set &&
        SymbolEqualityComparer.Default.Equals(set.OriginalDefinition, DbSet)
            ? set.TypeArguments[0]
            : null;

    /// <summary>
    /// The context and entity type of a <c>modelBuilder.Entity&lt;T&gt;()</c> call in one of a context's methods, which maps
    /// the entity type to that context.
    /// </summary>
    public (INamedTypeSymbol Context, ITypeSymbol Entity)? MappedEntity(IInvocationOperation invocation, ISymbol containingSymbol)
    {
        var method = invocation.TargetMethod;

        return method is { Name: "Entity", IsGenericMethod: true, TypeArguments.Length: 1 } &&
               IsDeclaredBy(method, ModelBuilder) &&
               containingSymbol.ContainingType is { } owner &&
               DerivesFrom(owner, DbContext)
            ? (owner, method.TypeArguments[0])
            : null;
    }
}
