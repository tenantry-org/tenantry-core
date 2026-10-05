using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tenantry.EfCore.Analyzers;

/// <summary>
/// TNY3001: <c>MakeCurrent</c> or <c>CreateScope</c> given a descriptor created in the call itself
/// (<c>new TenantDescriptor&lt;TKey&gt; { ... }</c>), not one read from the store. A descriptor in a variable or a field is
/// not reported, since it may well have been read from the store.
/// TNY3002: blocking on <c>RunInScopeAsync</c> in the expression that calls it, with <c>.Result</c>, <c>.Wait()</c> or
/// <c>.GetAwaiter().GetResult()</c> (through <c>ConfigureAwait</c> too).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantScopeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.InlineTenantDescriptor, Rules.BlockingRunInScope);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = KnownTypes.For(start.Compilation);

            if (types.TenantScopeFactory is null || types.TenantContextSetter is null)
                return;

            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, types), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzeResult(operation, types), OperationKind.PropertyReference);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        switch (method.Name)
        {
            case "MakeCurrent" when KnownTypes.IsDeclaredBy(method, types.TenantContextSetter):
            case "CreateScope" when KnownTypes.IsDeclaredBy(method, types.TenantScopeFactory):
                if (invocation.Arguments.Length == 1 && KnownTypes.Unwrap(invocation.Arguments[0].Value) is IObjectCreationOperation)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rules.InlineTenantDescriptor, invocation.Arguments[0].Syntax.GetLocation(), method.Name));
                }

                break;

            case "Wait" when IsRunInScope(invocation.Instance, types):
            case "GetResult" when method.Parameters.IsEmpty && invocation.Instance is IInvocationOperation
            {
                TargetMethod.Name: "GetAwaiter",
            } awaiter && IsRunInScope(Unconfigured(awaiter.Instance), types):
                context.ReportDiagnostic(Diagnostic.Create(Rules.BlockingRunInScope, invocation.Syntax.GetLocation()));
                break;
        }
    }

    private static void AnalyzeResult(OperationAnalysisContext context, KnownTypes types)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;

        if (reference.Property.Name == "Result" && IsRunInScope(reference.Instance, types))
            context.ReportDiagnostic(Diagnostic.Create(Rules.BlockingRunInScope, reference.Syntax.GetLocation()));
    }

    private static bool IsRunInScope(IOperation? operation, KnownTypes types) =>
        KnownTypes.Unwrap(operation) is IInvocationOperation { TargetMethod.Name: "RunInScopeAsync" } call &&
        KnownTypes.IsDeclaredBy(call.TargetMethod, types.TenantScopeFactory);

    // RunInScopeAsync(...).ConfigureAwait(...) is still RunInScopeAsync's task.
    private static IOperation? Unconfigured(IOperation? operation) =>
        KnownTypes.Unwrap(operation) is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } task } ? task : operation;
}
