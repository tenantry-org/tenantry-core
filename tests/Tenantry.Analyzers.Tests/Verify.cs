using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

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

    private static readonly MetadataReference[] WithoutTenantry =
    [
        .. References.Where(reference => !Path.GetFileName(reference.Display!).StartsWith("Tenantry.", StringComparison.Ordinal)),
    ];

    public static Task AnalyzerAsync<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        AnalyzerWithLibrariesAsync<TAnalyzer>(source, [], expected);

    /// <summary>As <see cref="AnalyzerAsync{TAnalyzer}"/>, with more files in the same project, such as generated ones.</summary>
    public static Task AnalyzerWithFilesAsync<TAnalyzer>(
        string source,
        IEnumerable<(string Name, string Content)> files,
        params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        RunAsync<TAnalyzer>(source, files, [], expected);

    /// <summary>
    /// As <see cref="AnalyzerAsync{TAnalyzer}"/>, with the test code referencing libraries compiled from source, as an
    /// application references other packages, or projects.
    /// </summary>
    public static Task AnalyzerWithLibrariesAsync<TAnalyzer>(
        string source,
        IEnumerable<Library> libraries,
        params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        RunAsync<TAnalyzer>(source, [], libraries, expected);

    private static async Task RunAsync<TAnalyzer>(
        string source,
        IEnumerable<(string Name, string Content)> files,
        IEnumerable<Library> libraries,
        DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> test = new() { TestCode = source, ReferenceAssemblies = None };
        test.TestState.Sources.AddRange(files.Select(file => (file.Name, SourceText.From(file.Content))));
        test.TestState.AdditionalReferences.AddRange(References);
        Dictionary<string, MetadataReference> referenceAssemblies = [];

        foreach (var library in libraries)
        {
            var references = library.ReferencesTenantry ? References : WithoutTenantry;

            if (library.AsReferenceAssembly)
            {
                // As dotnet build references another project: its reference assembly, which lists only the assemblies
                // its signatures use.
                var image = ReferenceAssembly(library, [.. references, .. library.Uses.Select(name => referenceAssemblies[name])]);
                referenceAssemblies[library.Name] = image;
                test.TestState.AdditionalReferences.Add(image);
                continue;
            }

            // As an IDE references another project: its compilation.
            ProjectState project = new(library.Name, LanguageNames.CSharp, library.Name, "cs") { ReferenceAssemblies = None };
            project.Sources.Add(library.Source);
            project.AdditionalReferences.AddRange(references);
            project.AdditionalProjectReferences.AddRange(library.Uses);
            test.TestState.AdditionalProjects.Add(library.Name, project);
            test.TestState.AdditionalProjectReferences.Add(library.Name);
        }

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static PortableExecutableReference ReferenceAssembly(Library library, MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            library.Name,
            [CSharpSyntaxTree.ParseText(library.Source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream image = new();
        var result = compilation.Emit(image, options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>
    /// A library of the test code's, which references Tenantry's assemblies or not, and the libraries it uses. Referenced
    /// as a compilation, as an IDE does, or as an emitted reference assembly, as dotnet build does; a library referenced
    /// as a reference assembly uses only others that are.
    /// </summary>
    public sealed record Library(string Name, string Source, bool ReferencesTenantry = true, bool AsReferenceAssembly = false)
    {
        public string[] Uses { get; init; } = [];
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
