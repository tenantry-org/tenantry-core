using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1002: <c>IgnoreQueryFilters()</c> in a query that reads a tenant-owned entity type, as the type it queries or as a
/// type its calls bring in: an <c>Include</c> or <c>ThenInclude</c>, a <c>Select</c>, <c>SelectMany</c>, <c>Join</c> or
/// <c>GroupJoin</c>, or a navigation or query in one of its lambdas. With filter names (EF Core 10), it is reported only
/// when a name that is a constant equal to the tenant filter's is among them, so a query that ignores other filters and
/// keeps the tenant's is not.
/// </summary>
/// <remarks>
/// EF Core ignores the filters for the whole query wherever the call is, so every call that makes up the query in the one
/// expression the call is in is checked (<see cref="Query"/>). A query composed across statements, kept in a local, or
/// chosen with a conditional is not followed.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersAnalyzer : DiagnosticAnalyzer
{
    // TenantryQueryFilters.Tenant
    private const string TenantFilterName = "Tenantry.Tenant";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.IgnoreQueryFilters);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantEntity is not null && types.QueryableExtensions is not null && types.Queryable is not null)
                start.RegisterOperationAction(operation => Analyze(operation, types), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method is not { Name: "IgnoreQueryFilters", IsGenericMethod: true, TypeArguments.Length: 1 } ||
            !KnownTypes.IsDeclaredBy(method, types.QueryableExtensions))
        {
            return;
        }

        // IgnoreQueryFilters(source) ignores every filter; IgnoreQueryFilters(source, names) only those named.
        if (invocation.Arguments.Length > 1 && !NamesTenantFilter(invocation.Arguments[1], context))
            return;

        foreach (var call in Query(invocation, types))
        {
            if (TenantOwnedIn(call, types) is { } owned)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.IgnoreQueryFilters, invocation.Syntax.GetLocation(), owned.Name));
                return;
            }
        }
    }

    private static bool NamesTenantFilter(IArgumentOperation names, OperationAnalysisContext context)
    {
        var model = names.SemanticModel;

        if (model is null)
            return false;

        foreach (var node in names.Value.Syntax.DescendantNodesAndSelf())
        {
            if (model.GetConstantValue(node, context.CancellationToken) is { HasValue: true, Value: TenantFilterName })
                return true;
        }

        return false;
    }

    // The calls that make up the query in the one expression the call is in, which EF Core ignores the filters for
    // wherever the call is: the call; the calls before it in each query it takes (the query it is called on, a Join's
    // inner query, the other query of a Union, Concat, Intersect or Except); and the calls after it that take its result.
    // Through casts, and nothing across statements.
    private static IEnumerable<IInvocationOperation> Query(IInvocationOperation start, KnownTypes types)
    {
        var seen = new HashSet<IOperation>();
        var pending = new Stack<IInvocationOperation>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var call = pending.Pop();

            if (!seen.Add(call))
                continue;

            yield return call;

            foreach (var argument in call.Arguments)
            {
                if (WithoutConversions(argument.Value) is IInvocationOperation before && TakesQuery(before, types))
                    pending.Push(before);
            }

            if (Consumer(call) is { } after && TakesQuery(after, types))
                pending.Push(after);
        }
    }

    // The call the result of this one is passed to, if the expression goes on.
    private static IInvocationOperation? Consumer(IOperation operation)
    {
        while (operation.Parent is IConversionOperation conversion)
            operation = conversion;

        return operation.Parent is IArgumentOperation { Parent: IInvocationOperation call } ? call : null;
    }

    private static IOperation WithoutConversions(IOperation value)
    {
        while (value is IConversionOperation conversion)
            value = conversion.Operand;

        return value;
    }

    // Whether the call is an extension method on a query, which EF Core translates, rather than on results in memory.
    private static bool TakesQuery(IInvocationOperation call, KnownTypes types) =>
        call.TargetMethod is { IsExtensionMethod: true, Parameters.Length: > 0 } method && IsQuery(method.Parameters[0].Type, types);

    // The first tenant-owned type the call brings into the query: one of its type arguments, if it returns a query (the
    // type it queries, what an Include includes, what a Select selects, what a Join joins), a navigation an Include names
    // in a string, or a navigation or a query in one of its lambdas.
    private static ITypeSymbol? TenantOwnedIn(IInvocationOperation call, KnownTypes types)
    {
        var method = call.TargetMethod;

        if (IsQuery(call.Type, types) && FirstTenantOwned(method.TypeArguments, types) is { } typeArgument)
            return typeArgument;

        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Type is not { } parameter)
                continue;

            // Include("Categories.Purchases"): the navigations it names.
            if (parameter.SpecialType == SpecialType.System_String && method.Name == "Include" &&
                KnownTypes.IsDeclaredBy(method, types.QueryableExtensions) &&
                argument.Value.ConstantValue is { HasValue: true, Value: string path } &&
                FirstTenantOwned(Navigations(method.TypeArguments[0], path), types) is { } navigation)
            {
                return navigation;
            }

            // A lambda EF Core translates: what it reads from its parameters, and the queries it runs.
            if (!SymbolEqualityComparer.Default.Equals(parameter.OriginalDefinition, types.Expression))
                continue;

            foreach (var operation in argument.Value.DescendantsAndSelf())
            {
                if (operation.Type is { } type && (IsQuery(type, types) || FromLambdaParameter(operation)) &&
                    TenantOwned(type, types) is { } owned)
                {
                    return owned;
                }
            }
        }

        return null;
    }

    // The type of each navigation along an Include path, from the type the query reads, as far as each is found.
    private static IEnumerable<ITypeSymbol> Navigations(ITypeSymbol root, string path)
    {
        var type = root;

        foreach (var name in path.Split('.'))
        {
            var property = Members(type, name).OfType<IPropertySymbol>().FirstOrDefault();

            if (property is null)
                yield break;

            yield return property.Type;
            type = ElementType(property.Type);
        }
    }

    private static IEnumerable<ISymbol> Members(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
                yield return member;
        }
    }

    // A collection navigation's element type, or the type itself.
    private static ITypeSymbol ElementType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return type;

        var enumerable = type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            ? type as INamedTypeSymbol
            : type.AllInterfaces.FirstOrDefault(candidate =>
                candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

        return enumerable?.TypeArguments[0] ?? type;
    }

    // The tenant-owned type that type is, or holds as a type argument (a collection's, a query's).
    private static ITypeSymbol? TenantOwned(ITypeSymbol type, KnownTypes types)
    {
        if (KnownTypes.Implements(type, types.TenantEntity))
            return type;

        return type is INamedTypeSymbol named ? FirstTenantOwned(named.TypeArguments, types) : null;
    }

    private static ITypeSymbol? FirstTenantOwned(IEnumerable<ITypeSymbol> candidates, KnownTypes types) =>
        candidates.Select(type => TenantOwned(type, types)).FirstOrDefault(owned => owned is not null);

    private static bool IsQuery(ITypeSymbol? type, KnownTypes types) =>
        type is not null &&
        (SymbolEqualityComparer.Default.Equals(type, types.Queryable) ||
         type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, types.Queryable)));

    // Whether the value is read from a lambda's parameter, through properties and fields, as a navigation is; not the
    // parameter itself, which may be an element of a collection in memory.
    private static bool FromLambdaParameter(IOperation operation)
    {
        if (operation is not (IPropertyReferenceOperation or IFieldReferenceOperation))
            return false;

        while (true)
        {
            switch (operation)
            {
                case IPropertyReferenceOperation { Instance: { } instance }:
                    operation = instance;
                    break;
                case IFieldReferenceOperation { Instance: { } instance }:
                    operation = instance;
                    break;
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IParameterReferenceOperation reference:
                    return reference.Parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction };
                default:
                    return false;
            }
        }
    }
}
