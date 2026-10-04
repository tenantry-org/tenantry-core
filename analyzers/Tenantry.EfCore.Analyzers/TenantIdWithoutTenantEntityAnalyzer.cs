using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1001: an entity type a DbContext maps (a <c>DbSet&lt;T&gt;</c> property, or <c>modelBuilder.Entity&lt;T&gt;()</c>)
/// that has a <c>TenantId</c> property but does not implement <c>ITenantEntity&lt;TKey&gt;</c>. Not reported for types
/// marked shared, for tenant descriptors (a tenant registry's rows), or where <c>TenantId</c> is the type's key by EF
/// Core's convention (<c>Tenant.TenantId</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantIdWithoutTenantEntityAnalyzer : DiagnosticAnalyzer
{
    internal const string TenantIdProperty = "TenantId";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.TenantIdWithoutTenantEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is null || types.DbContext is null)
                return;

            if (types.DbSet is not null)
            {
                start.RegisterSymbolAction(symbol => AnalyzeProperty(symbol, types), SymbolKind.Property);
            }

            if (types.ModelBuilder is not null)
            {
                start.RegisterOperationAction(operation => AnalyzeEntityCall(operation, types), OperationKind.Invocation);
            }
        });
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, KnownTypes types)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (property.Type is INamedTypeSymbol { IsGenericType: true } set &&
            SymbolEqualityComparer.Default.Equals(set.OriginalDefinition, types.DbSet) &&
            KnownTypes.DerivesFrom(property.ContainingType, types.DbContext) &&
            UnownedTenantId(set.TypeArguments[0], types) is { } tenantId)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.TenantIdWithoutTenantEntity,
                property.Locations.FirstOrDefault(),
                Properties(set.TypeArguments[0]),
                set.TypeArguments[0].Name,
                tenantId.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeEntityCall(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method is { Name: "Entity", IsGenericMethod: true, TypeArguments.Length: 1 } &&
            KnownTypes.IsDeclaredBy(method, types.ModelBuilder) &&
            UnownedTenantId(method.TypeArguments[0], types) is { } tenantId)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.TenantIdWithoutTenantEntity,
                invocation.Syntax.GetLocation(),
                Properties(method.TypeArguments[0]),
                method.TypeArguments[0].Name,
                tenantId.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    // The entity type's TenantId property, when the type should implement ITenantEntity<TKey> and does not.
    private static IPropertySymbol? UnownedTenantId(ITypeSymbol type, KnownTypes types)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } entity ||
            KnownTypes.Implements(entity, types.TenantEntity) ||
            KnownTypes.Implements(entity, types.TenantDescriptor) ||
            IsMarkedShared(entity, types.SharedAcrossTenants) ||
            entity.Name + "Id" == TenantIdProperty)
        {
            return null;
        }

        for (var current = (INamedTypeSymbol?)entity; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(TenantIdProperty))
            {
                if (member is IPropertySymbol { IsStatic: false, GetMethod: not null } property)
                    return property;
            }
        }

        return null;
    }

    private static bool IsMarkedShared(INamedTypeSymbol type, INamedTypeSymbol? attribute)
    {
        if (attribute is null)
            return false;

        for (var current = (INamedTypeSymbol?)type; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute)))
                return true;
        }

        return false;
    }

    // Where the entity type is declared, for the code fix.
    private static ImmutableDictionary<string, string?> Properties(ITypeSymbol entity) =>
        ImmutableDictionary<string, string?>.Empty.Add(
            EntityTypeKey, entity.GetDocumentationCommentId());

    /// <summary>The diagnostic property that names the entity type, by its documentation comment id.</summary>
    internal const string EntityTypeKey = "EntityType";
}
