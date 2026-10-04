using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY1003: EF Core's raw SQL on <c>Database</c>, which maps to no entity type, so no tenant filter or save check applies.
/// <c>FromSql</c> on a set is not reported: EF Core applies the entity's query filters over it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> Methods = ImmutableHashSet.Create(
        "ExecuteSql",
        "ExecuteSqlAsync",
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync",
        "ExecuteSqlInterpolated",
        "ExecuteSqlInterpolatedAsync",
        "SqlQuery",
        "SqlQueryRaw");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.RawSql);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            // Only where Tenantry's EF Core integration is referenced, which is what isolates the context.
            if (types.TenantEntity is not null && types.SharedAcrossTenants is not null &&
                types.RelationalDatabaseFacadeExtensions is not null)
            {
                start.RegisterOperationAction(operation => Analyze(operation, types), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (Methods.Contains(method.Name) && KnownTypes.IsDeclaredBy(method, types.RelationalDatabaseFacadeExtensions))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.RawSql, invocation.Syntax.GetLocation(), method.Name));
        }
    }
}
