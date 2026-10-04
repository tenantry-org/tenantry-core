using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Tenantry.Analyzers.Tests;

/// <summary>
/// Runs an analyzer over test code compiled against the assemblies this test process runs with: the framework's, and
/// Tenantry's, EF Core's and ASP.NET Core's for the test's target framework. So a rule is checked against the real
/// signatures, as an application sees them, and nothing is downloaded.
/// </summary>
internal static class Verify
{
    // No reference package to download: the references below are the framework's own.
    private static readonly ReferenceAssemblies None = new("none");

    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => IsReference(Path.GetFileName(path)))
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    public static async Task AnalyzerAsync<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> test = new() { TestCode = source, ReferenceAssemblies = None };
        test.TestState.AdditionalReferences.AddRange(References);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static async Task CodeFixAsync<TAnalyzer, TCodeFix>(string source, string fixedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> test = new()
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = None,
        };
        test.TestState.AdditionalReferences.AddRange(References);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // The framework, and the libraries the rules are about; not the test tooling, which would only slow the compilation.
    private static bool IsReference(string file) =>
        file is "mscorlib.dll" or "netstandard.dll" ||
        (file.StartsWith("System.", StringComparison.Ordinal) && !file.StartsWith("System.Composition", StringComparison.Ordinal)) ||
        file.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
        file.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
        file.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
        file.StartsWith("Microsoft.Net.Http.Headers", StringComparison.Ordinal) ||
        file is "Tenantry.Core.dll" or "Tenantry.AspNetCore.dll" or "Tenantry.EfCore.dll";
}
