using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1004: <c>AddDbContext</c>, <c>AddDbContextPool</c>, <c>AddDbContextFactory</c> or
/// <c>AddPooledDbContextFactory</c> for a context with tenant-owned entities, with options that do not call
/// <c>UseTenantry()</c>. A context has tenant-owned entities when it, or a base context, has a <c>DbSet&lt;T&gt;</c>
/// property of a tenant-owned type, or maps one with <c>modelBuilder.Entity&lt;T&gt;()</c> in one of its methods.
/// </summary>
/// <remarks>
/// <para>
/// To keep false reports out, it reports a registration only when it sees everything its options do: they are a lambda,
/// or a method of the compilation's that cannot be overridden, that neither calls <c>UseTenantry()</c> nor hands an
/// options builder to code that could, nor assigns one anywhere. Code that could is a delegate, an interface, virtual or
/// unsealed override method, a local function or lambda, a method the compiler declares (a C# 14 extension member
/// called in static form), a constructor or method of an assembly that references Tenantry.EfCore (directly or through
/// others) or of another project's reference assembly, or a property of such an assembly or of the application's read
/// on the builder (a C# 14 extension property, or a builder subclass's); a method of the application's, a C# 14
/// extension method called on the builder among them, instead counts when it does any of this itself, decided once
/// every method is seen. A registration without options, or with a delegate it cannot see into, is not reported.
/// Generated code counts for what it does, and nothing in it is reported.
/// </para>
/// <para>
/// From EF Core 9, a context declared in another assembly is not reported: a registration or
/// <c>ConfigureDbContext</c> there, which it cannot see, adds to the options. Nor is a context with an
/// <c>OnConfiguring</c> in its hierarchy that it cannot see (in another assembly) or that may
/// apply <c>UseTenantry()</c> by the same test. A call that may apply it for the same context, or for a type parameter
/// the context satisfies, clears a registration: from EF Core 9, any registration or <c>ConfigureDbContext</c> in the
/// compilation, since their options add up; on EF Core 8, where only the first registration's options apply, one that is not later in the
/// same body (a lambda's or local function's being its own). So it reports at the end of the compilation, when every call
/// has been seen.
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
        // Generated code is read for what it does (a UseTenantry() helper, an OnConfiguring), and nothing in it is reported.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is null || types.DbContext is null || types.DbSet is null ||
                types.DbContextOptionsBuilder is null || types.ServiceCollectionExtensions is null)
            {
                return;
            }

            var state = new State(start.Compilation);

            start.RegisterOperationAction(
                operation => Collect(operation, types, state),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.SimpleAssignment,
                OperationKind.PropertyReference);
            start.RegisterCompilationEndAction(end => Report(end, types, state));
        });
    }

    private static void Collect(OperationAnalysisContext context, KnownTypes types, State state)
    {
        // What each method does with an options builder (its lambdas' and local functions' code included), for the options
        // methods and OnConfiguring judged at the end.
        var owner = context.ContainingSymbol is IMethodSymbol member ? Key(member) : context.ContainingSymbol.OriginalDefinition;
        var (applies, target) = HandOff(context.Operation, types, state);

        if (applies)
            state.DirectlyApplying.TryAdd(owner, 0);
        else if (target is not null)
            state.HandOffs.Add((owner, target));

        if (context.Operation is not IInvocationOperation invocation)
            return;

        if (types.MappedEntity(invocation, context.ContainingSymbol) is { } mapped &&
            KnownTypes.Implements(mapped.Entity, types.TenantEntity))
        {
            state.MapsTenantOwned.TryAdd(mapped.Context.OriginalDefinition, 0);
        }

        var method = invocation.TargetMethod;

        if (!KnownTypes.IsDeclaredBy(method, types.ServiceCollectionExtensions))
            return;

        var registration = Registrations.Contains(method.Name);

        if (!registration && method.Name != "ConfigureDbContext")
            return;

        var contextType = ContextType(method, types);
        var options = Options(invocation);

        // No options: the call applies nothing.
        if (contextType is null || options is null || options.ConstantValue is { HasValue: true, Value: null })
            return;

        var call = new Call(contextType, registration ? invocation.Syntax.GetLocation() : null, invocation);

        switch (KnownTypes.Unwrap(options))
        {
            case IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }:
                Judge(call, lambda, types, state);
                break;

            case IDelegateCreationOperation { Target: IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } reference }
                when LocalFunction(invocation, reference.Method) is { } local:
                Judge(call, local, types, state);
                break;

            // A method of the application's that cannot be overridden, and that the compiler did not declare.
            case IDelegateCreationOperation
            {
                Target: IMethodReferenceOperation
                {
                    Method:
                    {
                        MethodKind: MethodKind.Ordinary, IsVirtual: false, IsAbstract: false, IsOverride: false,
                        IsImplicitlyDeclared: false,
                    } optionsMethod,
                },
            }
                when SymbolEqualityComparer.Default.Equals(optionsMethod.ContainingAssembly, state.Compilation.Assembly):
                call.Targets.Add(Key(optionsMethod));
                break;

            // A delegate it cannot see into, or a method that may be overridden anywhere.
            default:
                call.Applies = true;
                break;
        }

        state.Calls.Add(call);
    }

    // What the options' code does with the builder.
    private static void Judge(Call call, IOperation body, KnownTypes types, State state)
    {
        foreach (var operation in body.Descendants())
        {
            var (applies, target) = HandOff(operation, types, state);

            if (applies)
            {
                call.Applies = true;
                return;
            }

            if (target is not null)
                call.Targets.Add(target);
        }
    }

    // What the operation does with an options builder: hands it to code that may apply UseTenantry() (UseTenantry()
    // itself among it), or to a method of the application's, decided once every method is seen, or to nothing that could.
    private static (bool Applies, IMethodSymbol? Target) HandOff(IOperation operation, KnownTypes types, State state)
    {
        switch (operation)
        {
            // A C# 14 extension member takes the builder as its receiver, not as an argument.
            case IInvocationOperation call
                when HasBuilder(call.Arguments, types) || OnBuilder(call.Instance, call.TargetMethod, types, state):
                var method = call.TargetMethod;

                // A delegate's, an interface's or a virtual method: the code that runs may be the application's.
                if (call.IsVirtual && (method.IsAbstract || method.IsVirtual || (method.IsOverride && !method.IsSealed)))
                    return (true, null);

                // A method of the application's, judged once every method is seen; a local function, a lambda or a
                // method the compiler declares (a C# 14 extension member called in static form) is taken to apply it.
                if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, state.Compilation.Assembly))
                {
                    return method is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false }
                        ? (false, Key(method))
                        : (true, null);
                }

                return (ReachesTenantryEfCore(method.ContainingAssembly, state), null);

            // A constructor of the application's, or of a library that can reach Tenantry.EfCore, may keep the builder.
            case IObjectCreationOperation { Constructor: { } constructor } creation when HasBuilder(creation.Arguments, types):
                return (ReachesTenantryEfCore(constructor.ContainingAssembly, state), null);

            case ISimpleAssignmentOperation assignment when IsBuilder(assignment.Value, types):
                return (true, null);

            // A property on the builder, of the application's or of an assembly that can reach Tenantry.EfCore, such as a
            // C# 14 extension property, is taken to apply it.
            case IPropertyReferenceOperation reference when OnBuilder(reference.Instance, reference.Property, types, state):
                return (true, null);

            default:
                return (false, null);
        }
    }

    // Whether code in the assembly can call UseTenantry(): it is Tenantry.EfCore, or references it, directly or through
    // the assemblies it references, or it is another project's reference assembly, which lists only the references its
    // signatures use, as dotnet build compiles against. Decided once per assembly; an assembly found on a search that
    // fails cannot either.
    private static bool ReachesTenantryEfCore(IAssemblySymbol? assembly, State state)
    {
        if (assembly is null)
            return false;

        if (state.Reaches.TryGetValue(assembly, out var known))
            return known;

        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IAssemblySymbol>();
        pending.Push(assembly);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!visited.Add(current))
                continue;

            if (current.Name == TenantryEfCore || (state.Reaches.TryGetValue(current, out known) && known) ||
                IsProjectReferenceAssembly(current))
            {
                state.Reaches.TryAdd(assembly, true);
                return true;
            }

            if (state.Reaches.ContainsKey(current))
                continue;

            foreach (var module in current.Modules)
            {
                foreach (var referenced in module.ReferencedAssemblySymbols)
                    pending.Push(referenced);
            }
        }

        foreach (var current in visited)
            state.Reaches.TryAdd(current, false);

        return false;
    }

    // A reference assembly that is not the framework's: the framework's reference packs carry the attribute too.
    private static bool IsProjectReferenceAssembly(IAssemblySymbol assembly) =>
        assembly.Name is not ("mscorlib" or "netstandard" or "System") &&
        !assembly.Name.StartsWith("System.", StringComparison.Ordinal) &&
        !assembly.Name.StartsWith("Microsoft.", StringComparison.Ordinal) &&
        assembly.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute");

    private static void Report(CompilationAnalysisContext context, KnownTypes types, State state)
    {
        var applying = Applying(state);
        var configured = new List<Call>();
        var unconfigured = new List<Call>();

        foreach (var call in state.Calls)
        {
            if (call.Applies || call.Targets.Any(applying.Contains))
                configured.Add(call);
            else if (call.Location is not null)
                unconfigured.Add(call);
        }

        // EF Core 9 and later add every registration's options up; EF Core 8 keeps the first's.
        var addsUp = types.DbContextOptionsConfiguration is not null;

        foreach (var registration in unconfigured)
        {
            if (registration.Context is not INamedTypeSymbol contextType)
                continue;

            // From EF Core 9, a registration or ConfigureDbContext in another project, which this compilation cannot see,
            // adds to the options too, so a context declared in another project is not reported.
            if (addsUp && !SymbolEqualityComparer.Default.Equals(contextType.ContainingAssembly, context.Compilation.Assembly))
                continue;

            if (!configured.Any(other => Clears(other, registration, contextType, addsUp)) &&
                !ConfiguresItself(contextType, types, state, applying) &&
                HasTenantOwned(contextType, types, state))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.ContextWithoutUseTenantry, registration.Location, contextType.Name));
            }
        }
    }

    // The methods that may apply UseTenantry() to a builder: those that do it themselves, and those that hand a builder to
    // a method that does, in turn.
    private static HashSet<ISymbol> Applying(State state)
    {
        var applying = new HashSet<ISymbol>(state.DirectlyApplying.Keys, SymbolEqualityComparer.Default);
        var callers = state.HandOffs.ToLookup(
            handOff => handOff.Target, handOff => handOff.Owner, SymbolEqualityComparer.Default);
        var pending = new Stack<ISymbol>(applying);

        while (pending.Count > 0)
        {
            foreach (var caller in callers[pending.Pop()].Where(applying.Add))
                pending.Push(caller);
        }

        return applying;
    }

    // Whether a call that may apply UseTenantry() covers the registration: one for its context type, or for a type
    // parameter it satisfies, that is not on EF Core 8 later in the same method.
    private static bool Clears(Call other, Call registration, INamedTypeSymbol contextType, bool addsUp) =>
        (other.Context is ITypeParameterSymbol parameter
            ? Satisfies(contextType, parameter)
            : SymbolEqualityComparer.Default.Equals(other.Context, contextType)) &&
        (addsUp || other.Tree != registration.Tree || other.Body != registration.Body || other.Position < registration.Position);

    // Whether the context type meets the type parameter's constraints, taking one it cannot tell (generic) as met.
    private static bool Satisfies(INamedTypeSymbol contextType, ITypeParameterSymbol parameter)
    {
        foreach (var constraint in parameter.ConstraintTypes)
        {
            if (constraint is ITypeParameterSymbol)
                continue;

            var met = contextType.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, constraint.OriginalDefinition));

            for (var current = (INamedTypeSymbol?)contextType; !met && current is not null; current = current.BaseType)
                met = SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, constraint.OriginalDefinition);

            if (!met)
                return false;
        }

        return true;
    }

    // An OnConfiguring in the context's hierarchy that may apply UseTenantry(), or that it cannot see.
    private static bool ConfiguresItself(INamedTypeSymbol contextType, KnownTypes types, State state, HashSet<ISymbol> applying) =>
        Hierarchy(contextType, types).SelectMany(type => type.GetMembers("OnConfiguring")).Any(member =>
            !SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, state.Compilation.Assembly) ||
            applying.Contains(member is IMethodSymbol method ? Key(method) : member.OriginalDefinition));

    // A DbSet<T> property of a tenant-owned type, or modelBuilder.Entity<T>() of one, in the context or a base context.
    private static bool HasTenantOwned(INamedTypeSymbol contextType, KnownTypes types, State state) =>
        Hierarchy(contextType, types).Any(type =>
            state.MapsTenantOwned.ContainsKey(type.OriginalDefinition) ||
            types.DbSets(type).Any(set => KnownTypes.Implements(set.Entity, types.TenantEntity)));

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

    // The context a call registers or configures: AddDbContext<TContextService, TContextImplementation>'s last type
    // argument, AddDbContextFactory<TContext, TFactory>'s first; in a generic method, a type parameter constrained to a
    // context, when no type argument is a context type.
    private static ITypeSymbol? ContextType(IMethodSymbol method, KnownTypes types)
    {
        ITypeSymbol? parameter = null;

        for (var i = method.TypeArguments.Length - 1; i >= 0; i--)
        {
            switch (method.TypeArguments[i])
            {
                case INamedTypeSymbol type when KnownTypes.DerivesFrom(type, types.DbContext):
                    return type;
                case ITypeParameterSymbol candidate when parameter is null && candidate.ConstraintTypes.Any(constraint =>
                    SymbolEqualityComparer.Default.Equals(constraint, types.DbContext) ||
                    KnownTypes.DerivesFrom(constraint, types.DbContext)):
                    parameter = candidate;
                    break;
            }
        }

        return parameter;
    }

    private static IOperation? Options(IInvocationOperation invocation) =>
        invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Type.TypeKind == TypeKind.Delegate)?.Value;

    // A local function's declaration, in the same method as the call.
    private static ILocalFunctionOperation? LocalFunction(IOperation call, IMethodSymbol method)
    {
        var root = call;

        while (root.Parent is not null)
            root = root.Parent;

        return root.Descendants().OfType<ILocalFunctionOperation>()
            .FirstOrDefault(local => SymbolEqualityComparer.Default.Equals(local.Symbol, method));
    }

    // A method as both its calls and its own code name it: a generic method's definition, a partial method's declaration.
    private static IMethodSymbol Key(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        return definition.PartialDefinitionPart ?? definition;
    }

    private static bool HasBuilder(ImmutableArray<IArgumentOperation> arguments, KnownTypes types)
    {
        foreach (var argument in arguments)
        {
            if (IsBuilder(argument.Value, types))
                return true;
        }

        return false;
    }

    // Whether the member, called or read on an options builder, is the application's or of an assembly that can reach
    // Tenantry.EfCore (EF Core's own members are neither).
    private static bool OnBuilder(IOperation? instance, ISymbol member, KnownTypes types, State state) =>
        IsBuilder(instance, types) &&
        (SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, state.Compilation.Assembly) ||
         ReachesTenantryEfCore(member.ContainingAssembly, state));

    // Whether the value is an options builder (DbContextOptionsBuilder<TContext> too), as it is or before a conversion.
    private static bool IsBuilder(IOperation? value, KnownTypes types)
    {
        for (; value is not null; value = (value as IConversionOperation)?.Operand)
        {
            if (value.Type is { } type &&
                (SymbolEqualityComparer.Default.Equals(type, types.DbContextOptionsBuilder) ||
                 KnownTypes.DerivesFrom(type, types.DbContextOptionsBuilder)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A registration or ConfigureDbContext, and what its options do.</summary>
    private sealed class Call(ITypeSymbol context, Location? location, IOperation invocation)
    {
        public ITypeSymbol Context { get; } = context;

        /// <summary>A registration's location; none for ConfigureDbContext, which is not reported.</summary>
        public Location? Location { get; } = location;

        public SyntaxTree Tree { get; } = invocation.Syntax.SyntaxTree;

        /// <summary>
        /// The body the call is in, a lambda's or local function's being its own, and where in it: on EF Core 8 the first
        /// registration counts, and the order of calls in different bodies is not known.
        /// </summary>
        public TextSpan Body { get; } = Root(invocation).Syntax.Span;

        public int Position { get; } = invocation.Syntax.SpanStart;

        /// <summary>Whether its options may apply UseTenantry().</summary>
        public bool Applies { get; set; }

        /// <summary>The application's methods its options hand a builder to, which may apply it in turn.</summary>
        public List<IMethodSymbol> Targets { get; } = [];

        private static IOperation Root(IOperation operation)
        {
            while (operation.Parent is not null && operation is not (IAnonymousFunctionOperation or ILocalFunctionOperation))
                operation = operation.Parent;

            return operation;
        }
    }

    private sealed class State(Compilation compilation)
    {
        public Compilation Compilation { get; } = compilation;

        /// <summary>The methods whose own code may apply UseTenantry() to an options builder.</summary>
        public ConcurrentDictionary<ISymbol, byte> DirectlyApplying { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The methods that hand a builder to a method of the application's, with that method.</summary>
        public ConcurrentBag<(ISymbol Owner, IMethodSymbol Target)> HandOffs { get; } = [];

        /// <summary>The registrations and ConfigureDbContext calls.</summary>
        public ConcurrentBag<Call> Calls { get; } = [];

        /// <summary>The contexts whose methods map a tenant-owned type with modelBuilder.Entity&lt;T&gt;().</summary>
        public ConcurrentDictionary<ITypeSymbol, byte> MapsTenantOwned { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>Whether each assembly can call UseTenantry().</summary>
        public ConcurrentDictionary<IAssemblySymbol, bool> Reaches { get; } = new(SymbolEqualityComparer.Default);
    }
}
