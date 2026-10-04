using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1002: <c>IgnoreQueryFilters()</c> on a query of a tenant-owned entity type. With filter names (EF Core 10), it
/// is reported only when a name that is a constant equal to the tenant filter's is among them, so a query that ignores
/// other filters and keeps the tenant's is not.
/// </summary>
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

            if (types.TenantEntity is not null && types.QueryableExtensions is not null)
                start.RegisterOperationAction(operation => Analyze(operation, types), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method is not { Name: "IgnoreQueryFilters", IsGenericMethod: true, TypeArguments.Length: 1 } ||
            !KnownTypes.IsDeclaredBy(method, types.QueryableExtensions) ||
            !KnownTypes.Implements(method.TypeArguments[0], types.TenantEntity))
        {
            return;
        }

        // IgnoreQueryFilters(source) ignores every filter; IgnoreQueryFilters(source, names) only those named.
        if (invocation.Arguments.Length > 1 && !NamesTenantFilter(invocation.Arguments[1], context))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rules.IgnoreQueryFilters, invocation.Syntax.GetLocation(), method.TypeArguments[0].Name));
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
}
