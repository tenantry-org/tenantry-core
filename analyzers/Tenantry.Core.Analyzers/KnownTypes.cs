using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.Core.Analyzers;

/// <summary>The Tenantry types the analyzers look for, found once per compilation.</summary>
internal sealed class KnownTypes
{
    private KnownTypes(Compilation compilation)
    {
        TenantScopeFactory = compilation.GetTypeByMetadataName("Tenantry.ITenantScopeFactory`1");
        TenantContextSetter = compilation.GetTypeByMetadataName("Tenantry.ITenantContextSetter`1");
    }

    public INamedTypeSymbol? TenantScopeFactory { get; }

    public INamedTypeSymbol? TenantContextSetter { get; }

    public static KnownTypes For(Compilation compilation) => new(compilation);

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
}
