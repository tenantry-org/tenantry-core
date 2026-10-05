using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1004: <c>AddDbContext</c>, <c>AddDbContextPool</c>, <c>AddDbContextFactory</c> or
/// <c>AddPooledDbContextFactory</c> for a context with tenant-owned entities, with options that do not call
/// <c>UseTenantry()</c>. A context has tenant-owned entities when it, or a base context, has a <c>DbSet&lt;T&gt;</c>
/// property of a type that implements <c>ITenantEntity&lt;TKey&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// To keep false reports out, it reports a registration only when it sees everything its options do: they are a lambda,
/// or a method of the compilation's, that neither calls <c>UseTenantry()</c> nor hands an options builder to code that
/// could (code of an assembly that references Tenantry.EfCore, the application's own included, or a delegate, interface
/// or virtual method), nor assigns one anywhere. A registration without options, or with a delegate it cannot see into,
/// is not reported.
/// </para>
/// <para>
/// Nor is a context that overrides <c>OnConfiguring</c>, or whose base context does, or one that another registration or a
/// <c>ConfigureDbContext</c> in the compilation may call <c>UseTenantry()</c> for: from EF Core 9, they add to the same
/// options. Such a call for a context type it cannot tell, a generic method's type parameter, makes it report nothing.
/// So it reports at the end of the compilation, when every call has been seen.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextWithoutUseTenantryAnalyzer : DiagnosticAnalyzer
{
    private const string TenantryEfCore = "Tenantry.EfCore";

    private static readonly ImmutableHashSet<string> Registrations = ImmutableHashSet.Create(
        "AddDbContext", "AddDbContextPool", "AddDbContextFactory", "AddPooledDbContextFactory");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.ContextWithoutUseTenantry);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is null || types.DbContext is null || types.DbSet is null ||
                types.DbContextOptionsBuilder is null || types.ServiceCollectionExtensions is null)
            {
                return;
            }

            var state = new State();

            start.RegisterOperationAction(
                operation => Collect(operation, types, state),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.SimpleAssignment);
            start.RegisterCompilationEndAction(end => Report(end, types, state));
        });
    }

    private static void Collect(OperationAnalysisContext context, KnownTypes types, State state)
    {
        // For options given as a method: the members whose code may apply UseTenantry().
        if (MayApply(context.Operation, types))
            state.Applying.TryAdd(context.ContainingSymbol.OriginalDefinition, 0);

        if (context.Operation is not IInvocationOperation invocation ||
            !KnownTypes.IsDeclaredBy(invocation.TargetMethod, types.ServiceCollectionExtensions))
        {
            return;
        }

        var method = invocation.TargetMethod;
        var registration = Registrations.Contains(method.Name);

        if (!registration && method.Name != "ConfigureDbContext")
            return;

        // AddDbContext<TContextService, TContextImplementation> registers its last type argument, and
        // AddDbContextFactory<TContext, TFactory> its first.
        var contextType = method.TypeArguments.LastOrDefault(type =>
            type is ITypeParameterSymbol || KnownTypes.DerivesFrom(type, types.DbContext));
        var options = invocation.Arguments
            .FirstOrDefault(argument => argument.Parameter?.Type.TypeKind == TypeKind.Delegate)?.Value;

        // No options: the call applies nothing.
        if (contextType is null || options is null || options.ConstantValue is { HasValue: true, Value: null })
            return;

        var location = registration ? invocation.Syntax.GetLocation() : null;

        switch (Unwrap(options))
        {
            case IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }:
                if (lambda.Descendants().Any(operation => MayApply(operation, types)))
                    state.Configured.TryAdd(contextType, 0);
                else if (location is not null)
                    state.Unconfigured.Add((contextType, location));

                break;

            case IDelegateCreationOperation { Target: IMethodReferenceOperation reference }
                when SymbolEqualityComparer.Default.Equals(reference.Method.ContainingAssembly, context.Compilation.Assembly):
                state.ByMethod.Add((contextType, reference.Method, location));
                break;

            // A delegate it cannot see into.
            default:
                state.Configured.TryAdd(contextType, 0);
                break;
        }
    }

    private static void Report(CompilationAnalysisContext context, KnownTypes types, State state)
    {
        foreach (var (contextType, options, location) in state.ByMethod)
        {
            if (Applies(options, state))
                state.Configured.TryAdd(contextType, 0);
            else if (location is not null)
                state.Unconfigured.Add((contextType, location));
        }

        if (state.Configured.Keys.Any(type => type is ITypeParameterSymbol))
            return;

        foreach (var (contextType, location) in state.Unconfigured)
        {
            if (contextType is INamedTypeSymbol named && !state.Configured.ContainsKey(named) &&
                !DeclaresOnConfiguring(named, types) && HasTenantOwnedSet(named, types))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules.ContextWithoutUseTenantry, location, named.Name));
            }
        }
    }

    // Whether the operation may apply UseTenantry() to an options builder: it passes one to UseTenantry() or to code that
    // could call it, or stores one, where other code can reach it.
    private static bool MayApply(IOperation operation, KnownTypes types) => operation switch
    {
        IInvocationOperation call =>
            call.Arguments.Any(argument => IsBuilder(argument.Value, types)) && MayCallUseTenantry(call.TargetMethod),
        IObjectCreationOperation { Constructor: { } constructor } creation =>
            creation.Arguments.Any(argument => IsBuilder(argument.Value, types)) && MayCallUseTenantry(constructor),
        ISimpleAssignmentOperation assignment => IsBuilder(assignment.Value, types),
        _ => false,
    };

    // Tenantry.EfCore's own methods (UseTenantry()), those of an assembly that references it (the application's own
    // included), and a method whose code an application can supply: a delegate's, an interface's, a virtual one.
    private static bool MayCallUseTenantry(IMethodSymbol method) =>
        method.IsAbstract || method.IsVirtual ||
        (method.ContainingAssembly is { } assembly &&
         (assembly.Name == TenantryEfCore ||
          assembly.Modules.Any(module => module.ReferencedAssemblies.Any(reference => reference.Name == TenantryEfCore))));

    // Whether the options method, or the member it is local to, may apply UseTenantry().
    private static bool Applies(IMethodSymbol method, State state)
    {
        for (ISymbol? current = method; current is IMethodSymbol; current = current.ContainingSymbol)
        {
            if (state.Applying.ContainsKey(current.OriginalDefinition))
                return true;
        }

        return false;
    }

    // An OnConfiguring of its own, which can configure anything, UseTenantry() included.
    private static bool DeclaresOnConfiguring(INamedTypeSymbol contextType, KnownTypes types) =>
        Hierarchy(contextType, types).Any(type => !type.GetMembers("OnConfiguring").IsEmpty);

    private static bool HasTenantOwnedSet(INamedTypeSymbol contextType, KnownTypes types) =>
        Hierarchy(contextType, types).SelectMany(type => type.GetMembers()).Any(member =>
            member is IPropertySymbol { Type: INamedTypeSymbol { IsGenericType: true } set } &&
            SymbolEqualityComparer.Default.Equals(set.OriginalDefinition, types.DbSet) &&
            KnownTypes.Implements(set.TypeArguments[0], types.TenantEntity));

    // The context type and its base types, up to DbContext.
    private static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol contextType, KnownTypes types)
    {
        for (var current = contextType;
             current is not null && !SymbolEqualityComparer.Default.Equals(current, types.DbContext);
             current = current.BaseType)
        {
            yield return current;
        }
    }

    // Whether the value is an options builder, as it is or before a conversion (to object, or a cast).
    private static bool IsBuilder(IOperation? value, KnownTypes types)
    {
        for (; value is not null; value = (value as IConversionOperation)?.Operand)
        {
            if (SymbolEqualityComparer.Default.Equals(value.Type, types.DbContextOptionsBuilder))
                return true;
        }

        return false;
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;

        return operation;
    }

    private sealed class State
    {
        /// <summary>The members whose code may apply UseTenantry() to an options builder.</summary>
        public ConcurrentDictionary<ISymbol, byte> Applying { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The context types a registration or ConfigureDbContext may call UseTenantry() for.</summary>
        public ConcurrentDictionary<ITypeSymbol, byte> Configured { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The registrations whose options are seen not to call UseTenantry().</summary>
        public ConcurrentBag<(ITypeSymbol Context, Location Location)> Unconfigured { get; } = [];

        /// <summary>The calls whose options are a method of the compilation's, with the registration's location.</summary>
        public ConcurrentBag<(ITypeSymbol Context, IMethodSymbol Options, Location? Location)> ByMethod { get; } = [];
    }
}
