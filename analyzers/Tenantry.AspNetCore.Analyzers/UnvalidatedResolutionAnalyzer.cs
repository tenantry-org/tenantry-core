using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.AspNetCore.Analyzers;

/// <summary>
/// TNY2001: an <c>AddTenantry</c> that resolves the tenant from something any caller can send (a header, a route value,
/// the query string, the host or a subdomain) while nothing checks the caller may use that tenant.
/// </summary>
/// <remarks>
/// To keep false reports out, it reports nothing when the compilation adds an access validator anywhere
/// (<c>ValidateTenantAccess</c>, <c>ValidateTenantAccessByClaim</c>) or names <c>ITenantAccessValidator&lt;TKey&gt;</c>
/// at all, or when the <c>AddTenantry</c> lambda hands its builder, or the builder's
/// services, to code this analyzer cannot see into. So it reports at the end of the compilation, where it knows.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnvalidatedResolutionAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RequestResolvers = ImmutableHashSet.Create(
        "ResolveFromHeader", "ResolveFromRouteValue", "ResolveFromQueryString", "ResolveFromHost", "ResolveFromSubdomain");

    private static readonly ImmutableHashSet<string> Validators =
        ImmutableHashSet.Create("ValidateTenantAccess", "ValidateTenantAccessByClaim");

    // The assemblies whose builder methods are known to add no validator, other than the validators above.
    private static readonly ImmutableHashSet<string> TenantryAssemblies = ImmutableHashSet.Create(
        "Tenantry.Core", "Tenantry.AspNetCore", "Tenantry.EfCore", "Tenantry.Http", "Tenantry.Caching", "Tenantry.Options");

    private static readonly DiagnosticDescriptor Rule = new(
        "TNY2001",
        "The tenant is resolved from the request, and nothing validates the caller's access to it",
        "{0} lets any caller name any tenant, and nothing checks the caller may use it; add ValidateTenantAccessByClaim " +
        "or ValidateTenantAccess in the same AddTenantry, or, where any caller may use any tenant (a public site per " +
        "tenant, a test), set dotnet_diagnostic.TNY2001.severity = none in .editorconfig",
        "Tenantry",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A header, route value, query string, host or subdomain is chosen by the caller. Without an access " +
                     "validator, a caller who can reach the application can act as any tenant.",
        helpLinkUri: "https://tenantry.dev/docs/core/analyzers#tny2001",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var builderExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.TenantryAspNetCoreTenantBuilderExtensions");
            var addTenantry = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.TenantryServiceCollectionExtensions");
            var validator = compilation.GetTypeByMetadataName("Tenantry.AspNetCore.ITenantAccessValidator`1");
            var tenantBuilder = compilation.GetTypeByMetadataName("Tenantry.ITenantBuilder");

            if (builderExtensions is null || addTenantry is null || validator is null || tenantBuilder is null)
                return;

            var state = new State();

            start.RegisterOperationAction(
                operation => AnalyzeInvocation(operation, state, builderExtensions, addTenantry, tenantBuilder),
                OperationKind.Invocation);
            // Any mention of ITenantAccessValidator<TKey> is taken for a validator: a type that implements it, one
            // registered with AddScoped<ITenantAccessValidator<TKey>, ...>(), a typeof, a constructor parameter.
            start.RegisterSyntaxNodeAction(
                node =>
                {
                    if (((GenericNameSyntax)node.Node).Identifier.ValueText == "ITenantAccessValidator")
                        state.FoundValidator();
                },
                SyntaxKind.GenericName);
            start.RegisterCompilationEndAction(end =>
            {
                if (state.HasValidator)
                    return;

                foreach (var location in state.Resolvers)
                    end.ReportDiagnostic(Diagnostic.Create(Rule, location.Location, location.Method));
            });
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        State state,
        INamedTypeSymbol builderExtensions,
        INamedTypeSymbol addTenantry,
        INamedTypeSymbol tenantBuilder)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (Validators.Contains(method.Name) && SymbolEqualityComparer.Default.Equals(method.ContainingType, builderExtensions))
        {
            state.FoundValidator();
            return;
        }

        if (method.Name != "AddTenantry" || !SymbolEqualityComparer.Default.Equals(method.ContainingType, addTenantry))
            return;

        var configure = invocation.Arguments.Select(argument => Lambda(argument.Value)).FirstOrDefault(lambda => lambda is not null);

        if (configure is null)
            return;

        List<(Location Location, string Method)> resolvers = [];

        foreach (var operation in configure.Body.Descendants())
        {
            switch (operation)
            {
                case IInvocationOperation call when SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, builderExtensions) &&
                                                    RequestResolvers.Contains(call.TargetMethod.Name):
                    resolvers.Add((call.Syntax.GetLocation(), call.TargetMethod.Name));
                    break;

                // The builder handed to code of the application's or another package's: it may add a validator.
                case IInvocationOperation call when HandsOnBuilder(call, tenantBuilder):
                    return;

                // The builder's services, where a validator can be registered directly.
                case IPropertyReferenceOperation { Property.Name: "Services" } reference
                    when IsBuilder(reference.Instance?.Type, tenantBuilder):
                    return;
            }
        }

        foreach (var resolver in resolvers)
            state.Resolvers.Add(resolver);
    }

    private static IAnonymousFunctionOperation? Lambda(IOperation operation) => operation switch
    {
        IAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
        IConversionOperation conversion => Lambda(conversion.Operand),
        _ => null,
    };

    private static bool HandsOnBuilder(IInvocationOperation call, INamedTypeSymbol tenantBuilder)
    {
        var assembly = call.TargetMethod.ContainingAssembly?.Name;

        if (assembly is not null && TenantryAssemblies.Contains(assembly))
            return false;

        return call.Arguments.Any(argument => IsBuilder(argument.Value.Type, tenantBuilder)) ||
               IsBuilder(call.Instance?.Type, tenantBuilder);
    }

    private static bool IsBuilder(ITypeSymbol? type, INamedTypeSymbol tenantBuilder) =>
        type is not null &&
        (SymbolEqualityComparer.Default.Equals(type, tenantBuilder) ||
         type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, tenantBuilder)));

    private sealed class State
    {
        private int _hasValidator;

        public ConcurrentBag<(Location Location, string Method)> Resolvers { get; } = [];

        public bool HasValidator => Volatile.Read(ref _hasValidator) == 1;

        public void FoundValidator() => Volatile.Write(ref _hasValidator, 1);
    }
}
