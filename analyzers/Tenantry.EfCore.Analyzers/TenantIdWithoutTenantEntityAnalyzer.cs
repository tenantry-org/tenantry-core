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
/// <c>&lt;Type&gt;Id</c> property, a <c>[Key]</c>, or a <c>[PrimaryKey]</c> without <c>TenantId</c>).
/// </para>
/// <para>
/// A marker in a generic method, on its type parameter, marks the type each call in the compilation passes for it,
/// through generic methods that pass their own type parameter on. A marker whose type the analyzer cannot tell (a
/// builder of a type it cannot see, or a type parameter of a generic type) silences the contexts that apply it: the
/// context whose methods contain it, the contexts that call the method containing it or create the configuration
/// containing it, and the contexts derived from them. One that no context in the compilation applies, such as dead
/// code, silences nothing.
/// </para>
/// <para>
/// It reports each type once, where it is first mapped outside generated code, at the end of the compilation, when every
/// context and marker has been seen. Generated code counts for its contexts and markers.
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
        // Generated code is read for its contexts and markers, and nothing in it is reported.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is null || types.DbContext is null || types.DbSet is null || types.ModelBuilder is null)
                return;

            var state = new State();

            start.RegisterSymbolAction(symbol => CollectContext(symbol, types, state), SymbolKind.NamedType);
            start.RegisterSymbolAction(symbol => CollectDbSet(symbol, types, state), SymbolKind.Property);
            start.RegisterOperationAction(operation => CollectCall(operation, types, state), OperationKind.Invocation);
            start.RegisterOperationAction(operation => CollectConfiguration(operation, types, state), OperationKind.ObjectCreation);
            start.RegisterCompilationEndAction(end => Report(end, types, state));
        });
    }

    private static void CollectContext(SymbolAnalysisContext context, KnownTypes types, State state)
    {
        if (KnownTypes.DerivesFrom((INamedTypeSymbol)context.Symbol, types.DbContext))
            state.Contexts.TryAdd((INamedTypeSymbol)context.Symbol, 0);
    }

    // Each DbSet<T> property by itself, so that one in a generated part of a partial context, and only that one, counts
    // as generated.
    private static void CollectDbSet(SymbolAnalysisContext context, KnownTypes types, State state)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (types.DbSetEntity(property) is { } entity && property.ContainingType is { } type &&
            KnownTypes.DerivesFrom(type, types.DbContext))
        {
            state.Mapped.Add((type, entity, property.Locations.FirstOrDefault() ?? Location.None, context.IsGeneratedCode));
        }
    }

    private static void CollectCall(OperationAnalysisContext context, KnownTypes types, State state)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Calls between the application's own methods, to find which contexts apply a marker.
        if (method.OriginalDefinition.Locations.Any(location => location.IsInSource))
            state.Calls.Add((context.ContainingSymbol, method));

        if (types.MappedEntity(invocation, context.ContainingSymbol) is { } mapped)
            state.Mapped.Add((mapped.Context, mapped.Entity, invocation.Syntax.GetLocation(), context.IsGeneratedCode));

        switch (method.Name)
        {
            case "IsSharedAcrossTenants" when KnownTypes.IsDeclaredBy(method, types.EntityTypeBuilderExtensions):
                switch (MarkedType(invocation, types))
                {
                    case ITypeParameterSymbol { DeclaringMethod: not null } parameter:
                        state.GenericMarkers.Add(parameter);
                        break;
                    case null or ITypeParameterSymbol:
                        state.UnknownMarkers.Add(context.ContainingSymbol);
                        break;
                    case var marked:
                        state.MarkedShared.TryAdd(marked, 0);
                        break;
                }

                break;

            case "ApplyConfigurationsFromAssembly" when KnownTypes.IsDeclaredBy(method, types.ModelBuilder):
                state.AssemblyConfigurations.Add(context.ContainingSymbol);
                break;
        }
    }

    private static void CollectConfiguration(OperationAnalysisContext context, KnownTypes types, State state)
    {
        if (context.Operation.Type is { } created && KnownTypes.Implements(created, types.EntityTypeConfiguration))
            state.Configurations.Add((context.ContainingSymbol, created));
    }

    // The entity type IsSharedAcrossTenants() marks: EntityTypeBuilder<T>'s T, or typeof(T) in modelBuilder.Entity(typeof(T)).
    private static ITypeSymbol? MarkedType(IInvocationOperation invocation, KnownTypes types)
    {
        if (invocation.TargetMethod is { IsGenericMethod: true, TypeArguments.Length: 1 } generic)
            return generic.TypeArguments[0];

        var builder = invocation.Arguments.FirstOrDefault()?.Value;

        while (builder is IConversionOperation conversion)
            builder = conversion.Operand;

        return builder is IInvocationOperation { TargetMethod.Name: "Entity", Arguments.Length: 1 } entity &&
               entity.Arguments[0].Value is ITypeOfOperation typeOf &&
               KnownTypes.IsDeclaredBy(entity.TargetMethod, types.ModelBuilder)
            ? typeOf.TypeOperand
            : null;
    }

    private static void Report(CompilationAnalysisContext context, KnownTypes types, State state)
    {
        var silenced = SilencedContexts(types, state);

        var byContext = state.Mapped.ToLookup(mapping => mapping.Context, SymbolEqualityComparer.Default);

        // A context maps its own types and its base contexts'.
        IEnumerable<(ITypeSymbol Entity, Location Location, bool Generated)> MappedBy(INamedTypeSymbol contextType)
        {
            for (var current = (INamedTypeSymbol?)contextType; current is not null; current = current.BaseType)
            {
                foreach (var mapping in byContext[current])
                    yield return (mapping.Entity, mapping.Location, mapping.Generated);
            }
        }

        var reported = new Dictionary<ITypeSymbol, Location>(SymbolEqualityComparer.Default);

        foreach (var contextType in state.Contexts.Keys)
        {
            if (IsOrDerivesFromAny(contextType, silenced))
                continue;

            var mapped = MappedBy(contextType).ToList();

            if (!mapped.Any(mapping => KnownTypes.Implements(mapping.Entity, types.TenantEntity)))
                continue;

            foreach (var (entity, location, generated) in mapped)
            {
                if (generated || state.MarkedShared.ContainsKey(entity) || !ShouldBeTenantOwned(entity, types))
                    continue;

                KeepFirst(reported, entity, location);
            }
        }

        foreach (var pair in reported)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.TenantIdWithoutTenantEntity,
                pair.Value,
                pair.Key.Name,
                Advice(FindTenantId(pair.Key)!.Type, context.Compilation)));
        }
    }

    // What the diagnostic tells the developer to do, by whether the TenantId's type can be a tenant key.
    private static string Advice(ITypeSymbol tenantIdType, Compilation compilation)
    {
        var keyType = tenantIdType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return CanBeKey(tenantIdType, compilation)
            ? $"implement ITenantEntity<{keyType}>, or mark it [SharedAcrossTenants] if every tenant shares it"
            : $"its TenantId is a {keyType}, which cannot be a tenant key: make it a non-nullable Guid, int, long " +
              "or string and implement ITenantEntity<TKey>, or mark the type [SharedAcrossTenants] if every " +
              "tenant shares it";
    }

    // The contexts the unknown markers silence, after the generic markers are resolved to the types their calls pass.
    private static HashSet<INamedTypeSymbol> SilencedContexts(KnownTypes types, State state)
    {
        var callsTo = state.Calls.ToLookup(call => call.Callee.OriginalDefinition, SymbolEqualityComparer.Default);
        var unknown = state.UnknownMarkers.ToList();

        // A call that passes its caller's own type parameter marks whatever the caller's calls pass for it, in turn.
        var pending = new Stack<ITypeParameterSymbol>(state.GenericMarkers);
        var resolved = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);

        while (pending.Count > 0)
        {
            var parameter = pending.Pop();

            if (!resolved.Add(parameter))
                continue;

            foreach (var (caller, callee) in callsTo[parameter.DeclaringMethod!.OriginalDefinition])
            {
                switch (callee.TypeArguments[parameter.Ordinal])
                {
                    case ITypeParameterSymbol { DeclaringMethod: not null } passedOn:
                        pending.Push(passedOn);
                        break;
                    case ITypeParameterSymbol:
                        unknown.Add(caller);
                        break;
                    case var marked:
                        state.MarkedShared.TryAdd(marked, 0);
                        break;
                }
            }
        }

        var silenced = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var marker in unknown)
            silenced.UnionWith(ContextsApplying(marker, types, state, callsTo, new HashSet<ISymbol>(SymbolEqualityComparer.Default)));

        return silenced;
    }

    // The contexts whose model building reaches the code in symbol: its own context, or those that call it, or create
    // the configuration it belongs to (or apply every configuration in the assembly).
    private static HashSet<INamedTypeSymbol> ContextsApplying(
        ISymbol symbol,
        KnownTypes types,
        State state,
        ILookup<ISymbol?, (ISymbol Caller, IMethodSymbol Callee)> callsTo,
        HashSet<ISymbol> visited)
    {
        var contexts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (!visited.Add(symbol.OriginalDefinition))
            return contexts;

        if (symbol.ContainingType is { } type && KnownTypes.DerivesFrom(type, types.DbContext))
        {
            contexts.Add(type);
            return contexts;
        }

        var appliers = callsTo[symbol.OriginalDefinition].Select(call => call.Caller).ToList();

        if (symbol.ContainingType is { } configuration && KnownTypes.Implements(configuration, types.EntityTypeConfiguration))
        {
            appliers.AddRange(state.Configurations
                .Where(created => SymbolEqualityComparer.Default.Equals(created.Type.OriginalDefinition, configuration.OriginalDefinition))
                .Select(created => created.Creator));
            appliers.AddRange(state.AssemblyConfigurations);
        }

        foreach (var applier in appliers)
            contexts.UnionWith(ContextsApplying(applier, types, state, callsTo, visited));

        return contexts;
    }

    private static bool IsOrDerivesFromAny(INamedTypeSymbol type, HashSet<INamedTypeSymbol> contexts)
    {
        for (var current = (INamedTypeSymbol?)type; current is not null; current = current.BaseType)
        {
            if (contexts.Contains(current))
                return true;
        }

        return false;
    }

    // Once per type, where it is first mapped outside generated code (in source order, so the result does not depend on threads).
    private static void KeepFirst(Dictionary<ITypeSymbol, Location> reported, ITypeSymbol entity, Location location)
    {
        if (!reported.TryGetValue(entity, out var first) || Precedes(location, first))
            reported[entity] = location;
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
        public ConcurrentDictionary<INamedTypeSymbol, byte> Contexts { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The types each context maps, where, and whether that is in generated code, where nothing is reported.</summary>
        public ConcurrentBag<(INamedTypeSymbol Context, ITypeSymbol Entity, Location Location, bool Generated)> Mapped { get; } = [];

        public ConcurrentDictionary<ITypeSymbol, byte> MarkedShared { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The members containing a marker whose type the analyzer cannot tell.</summary>
        public ConcurrentBag<ISymbol> UnknownMarkers { get; } = [];

        /// <summary>The generic methods' type parameters a marker marks.</summary>
        public ConcurrentBag<ITypeParameterSymbol> GenericMarkers { get; } = [];

        /// <summary>The calls to the compilation's own methods, with the member making each.</summary>
        public ConcurrentBag<(ISymbol Caller, IMethodSymbol Callee)> Calls { get; } = [];

        /// <summary>The entity type configurations created, with the member creating each.</summary>
        public ConcurrentBag<(ISymbol Creator, ITypeSymbol Type)> Configurations { get; } = [];

        /// <summary>The members that apply every configuration in an assembly.</summary>
        public ConcurrentBag<ISymbol> AssemblyConfigurations { get; } = [];
    }
}
