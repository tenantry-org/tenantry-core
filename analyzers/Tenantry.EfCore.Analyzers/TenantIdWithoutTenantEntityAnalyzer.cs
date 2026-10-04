using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1001: an entity type that a DbContext maps, with a <c>TenantId</c> property, that does not implement
/// <c>ITenantEntity&lt;TKey&gt;</c>, in a context that maps at least one type that does.
/// </summary>
/// <remarks>
/// <para>
/// A context's types are those of its <c>DbSet&lt;T&gt;</c> properties (its own and its base contexts') and of the
/// <c>modelBuilder.Entity&lt;T&gt;()</c> calls in its methods. A context with no tenant-owned type among them (a
/// database-per-tenant context, or one Tenantry does not isolate) is left alone, as Tenantry checks nothing there.
/// </para>
/// <para>
/// Not reported: a type marked shared, by <c>[SharedAcrossTenants]</c> (on it or a base type) or by
/// <c>IsSharedAcrossTenants()</c> anywhere in the compilation; a tenant descriptor; and a type whose key is, or may be,
/// its <c>TenantId</c>, as a tenant registry's is: one with no other key by EF Core's conventions (an <c>Id</c> or
/// <c>&lt;Type&gt;Id</c> property, a <c>[Key]</c>, or a <c>[PrimaryKey]</c> without <c>TenantId</c>). A compilation that
/// calls <c>IsSharedAcrossTenants()</c> on a builder whose type the analyzer cannot tell reports nothing.
/// </para>
/// <para>
/// It reports each type once, where it is first mapped, at the end of the compilation, when every context and marker
/// has been seen.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantIdWithoutTenantEntityAnalyzer : DiagnosticAnalyzer
{
    private const string TenantIdProperty = "TenantId";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.TenantIdWithoutTenantEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is null || types.DbContext is null || types.DbSet is null || types.ModelBuilder is null)
                return;

            var state = new State();

            start.RegisterSymbolAction(symbol => CollectDbSets(symbol, types, state), SymbolKind.NamedType);
            start.RegisterOperationAction(operation => CollectCall(operation, types, state), OperationKind.Invocation);
            start.RegisterCompilationEndAction(end => Report(end, types, state));
        });
    }

    private static void CollectDbSets(SymbolAnalysisContext context, KnownTypes types, State state)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (!KnownTypes.DerivesFrom(type, types.DbContext))
            return;

        state.Contexts.TryAdd(type, 0);

        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol { Type: INamedTypeSymbol { IsGenericType: true } set } property &&
                SymbolEqualityComparer.Default.Equals(set.OriginalDefinition, types.DbSet))
            {
                state.Mapped.Add((type, set.TypeArguments[0], property.Locations.FirstOrDefault() ?? Location.None));
            }
        }
    }

    private static void CollectCall(OperationAnalysisContext context, KnownTypes types, State state)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        switch (method.Name)
        {
            case "Entity" when method is { IsGenericMethod: true, TypeArguments.Length: 1 } &&
                               KnownTypes.IsDeclaredBy(method, types.ModelBuilder) &&
                               context.ContainingSymbol.ContainingType is { } owner &&
                               KnownTypes.DerivesFrom(owner, types.DbContext):
                state.Mapped.Add((owner, method.TypeArguments[0], invocation.Syntax.GetLocation()));
                break;

            case "IsSharedAcrossTenants" when KnownTypes.IsDeclaredBy(method, types.EntityTypeBuilderExtensions):
                if (MarkedType(invocation, types) is { } marked)
                    state.MarkedShared.TryAdd(marked, 0);
                else
                    state.FoundUnknownMarker();
                break;
        }
    }

    // The entity type IsSharedAcrossTenants() marks: EntityTypeBuilder<T>'s T, or typeof(T) in modelBuilder.Entity(typeof(T)).
    private static ITypeSymbol? MarkedType(IInvocationOperation invocation, KnownTypes types)
    {
        if (invocation.TargetMethod is { IsGenericMethod: true, TypeArguments.Length: 1 } generic)
            return generic.TypeArguments[0];

        var builder = invocation.Arguments.FirstOrDefault()?.Value;

        while (builder is IConversionOperation conversion)
            builder = conversion.Operand;

        return builder is IInvocationOperation { TargetMethod.Name: "Entity" } entity &&
               entity.Arguments.Length == 1 &&
               entity.Arguments[0].Value is ITypeOfOperation typeOf &&
               KnownTypes.IsDeclaredBy(entity.TargetMethod, types.ModelBuilder)
            ? typeOf.TypeOperand
            : null;
    }

    private static void Report(CompilationAnalysisContext context, KnownTypes types, State state)
    {
        if (state.UnknownMarker)
            return;

        var byContext = state.Mapped.ToLookup(mapping => mapping.Context, SymbolEqualityComparer.Default);

        // A context maps its own types and its base contexts'.
        IEnumerable<(ITypeSymbol Entity, Location Location)> MappedBy(INamedTypeSymbol contextType)
        {
            for (var current = (INamedTypeSymbol?)contextType; current is not null; current = current.BaseType)
            {
                foreach (var mapping in byContext[current])
                    yield return (mapping.Entity, mapping.Location);
            }
        }

        var reported = new Dictionary<ITypeSymbol, Location>(SymbolEqualityComparer.Default);

        foreach (var contextType in state.Contexts.Keys)
        {
            var mapped = MappedBy(contextType).ToList();

            if (!mapped.Any(mapping => KnownTypes.Implements(mapping.Entity, types.TenantEntity)))
                continue;

            foreach (var (entity, location) in mapped)
            {
                if (state.MarkedShared.ContainsKey(entity) || !ShouldBeTenantOwned(entity, types))
                    continue;

                // Once per type, where it is first mapped (in source order, so the result does not depend on threads).
                if (!reported.TryGetValue(entity, out var first) || Precedes(location, first))
                    reported[entity] = location;
            }
        }

        foreach (var pair in reported)
        {
            var tenantId = FindTenantId(pair.Key)!;
            var keyType = tenantId.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            context.ReportDiagnostic(Diagnostic.Create(
                Rules.TenantIdWithoutTenantEntity,
                pair.Value,
                pair.Key.Name,
                CanBeKey(tenantId.Type, context.Compilation)
                    ? $"implement ITenantEntity<{keyType}>, or mark it [SharedAcrossTenants] if every tenant shares it"
                    : $"its TenantId is a {keyType}, which cannot be a tenant key: make it a non-nullable Guid, int, long " +
                      "or string and implement ITenantEntity<TKey>, or mark the type [SharedAcrossTenants] if every " +
                      "tenant shares it"));
        }
    }

    private static bool Precedes(Location location, Location other)
    {
        var path = string.CompareOrdinal(location.SourceTree?.FilePath, other.SourceTree?.FilePath);
        return path < 0 || (path == 0 && location.SourceSpan.Start < other.SourceSpan.Start);
    }

    // Whether the type has a TenantId but is neither tenant-owned, nor marked shared by attribute, nor a tenant
    // registry's type.
    private static bool ShouldBeTenantOwned(ITypeSymbol type, KnownTypes types) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class } entity &&
        !KnownTypes.Implements(entity, types.TenantEntity) &&
        !KnownTypes.Implements(entity, types.TenantDescriptor) &&
        !HasAttribute(entity, types.SharedAcrossTenants) &&
        FindTenantId(entity) is not null &&
        HasOtherKey(entity, types);

    private static IPropertySymbol? FindTenantId(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(TenantIdProperty))
            {
                if (member is IPropertySymbol { IsStatic: false, GetMethod: not null } property)
                    return property;
            }
        }

        return null;
    }

    // A key other than TenantId by EF Core's conventions, so TenantId is not the key, as it is a tenant registry's.
    private static bool HasOtherKey(INamedTypeSymbol entity, KnownTypes types)
    {
        if (entity.GetAttributes().FirstOrDefault(data =>
                SymbolEqualityComparer.Default.Equals(data.AttributeClass, types.PrimaryKeyAttribute)) is { } primaryKey)
        {
            return !primaryKey.ConstructorArguments
                .SelectMany(argument => argument.Kind == TypedConstantKind.Array ? argument.Values : ImmutableArray.Create(argument))
                .Any(argument => argument.Value as string == TenantIdProperty);
        }

        var properties = new List<IPropertySymbol>();

        for (var current = (ITypeSymbol?)entity; current is not null; current = current.BaseType)
            properties.AddRange(current.GetMembers().OfType<IPropertySymbol>());

        return !properties.Any(property => property.Name == TenantIdProperty && HasAttribute(property, types.KeyAttribute)) &&
               properties.Any(property => property.Name != TenantIdProperty &&
                                          (property.Name == "Id" || property.Name == entity.Name + "Id" ||
                                           HasAttribute(property, types.KeyAttribute)));
    }

    // TKey is IEquatable<TKey> and IParsable<TKey>: Guid, int, long and string are; Guid? is not.
    private static bool CanBeKey(ITypeSymbol type, Compilation compilation)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;

        var parsable = compilation.GetTypeByMetadataName("System.IParsable`1");
        var equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");

        return parsable is not null && equatable is not null &&
               type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parsable.Construct(type))) &&
               type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, equatable.Construct(type)));
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute)
    {
        if (attribute is null)
            return false;

        for (var current = symbol; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (current.GetAttributes().Any(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute)))
                return true;
        }

        return false;
    }

    private sealed class State
    {
        private int _unknownMarker;

        public ConcurrentDictionary<INamedTypeSymbol, byte> Contexts { get; } = new(SymbolEqualityComparer.Default);

        public ConcurrentBag<(INamedTypeSymbol Context, ITypeSymbol Entity, Location Location)> Mapped { get; } = [];

        public ConcurrentDictionary<ITypeSymbol, byte> MarkedShared { get; } = new(SymbolEqualityComparer.Default);

        public bool UnknownMarker => Volatile.Read(ref _unknownMarker) == 1;

        public void FoundUnknownMarker() => Volatile.Write(ref _unknownMarker, 1);
    }
}
