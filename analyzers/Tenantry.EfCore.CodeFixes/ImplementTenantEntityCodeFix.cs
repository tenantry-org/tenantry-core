using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Tenantry.EfCore.Analyzers;

namespace Tenantry.EfCore.CodeFixes;

/// <summary>
/// TNY1001's fix: makes the entity type implement <c>ITenantEntity&lt;TKey&gt;</c>, with <c>TKey</c> its
/// <c>TenantId</c>'s type, where the type is declared in the solution.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplementTenantEntityCodeFix))]
[Shared]
public sealed class ImplementTenantEntityCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Rules.TenantIdWithoutTenantEntity.Id);

    // Each fix edits another type's declaration, so fixing all at once is not offered.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        if (!diagnostic.Properties.TryGetValue(TenantIdWithoutTenantEntityAnalyzer.EntityTypeKey, out var id) || id is null)
            return;

        var compilation = await context.Document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);

        if (compilation is null ||
            DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) is not INamedTypeSymbol entity ||
            entity.DeclaringSyntaxReferences.FirstOrDefault() is not { } declaration ||
            compilation.GetTypeByMetadataName("Tenantry.ITenantEntity`1") is not { } tenantEntity ||
            FindTenantIdType(entity) is not { } keyType)
        {
            return;
        }

        var document = context.Document.Project.Solution.GetDocument(declaration.SyntaxTree);

        if (document is null)
            return;

        var title = $"Implement ITenantEntity<{keyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}>";
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => AddInterfaceAsync(document, declaration, tenantEntity.Construct(keyType), cancellationToken),
                equivalenceKey: nameof(ImplementTenantEntityCodeFix)),
            diagnostic);
    }

    private static ITypeSymbol? FindTenantIdType(INamedTypeSymbol entity)
    {
        for (var current = (INamedTypeSymbol?)entity; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(TenantIdWithoutTenantEntityAnalyzer.TenantIdProperty).OfType<IPropertySymbol>().FirstOrDefault() is
                { } property)
            {
                return property.Type;
            }
        }

        return null;
    }

    private static async Task<Solution> AddInterfaceAsync(
        Document document, SyntaxReference declaration, INamedTypeSymbol interfaceType, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var node = await declaration.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var type = editor.Generator.TypeExpression(interfaceType).WithAdditionalAnnotations(Simplifier.AddImportsAnnotation, Simplifier.Annotation);

        editor.AddInterfaceType(node, type);

        var changed = editor.GetChangedDocument();
        changed = await ImportAdder.AddImportsAsync(changed, Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        changed = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);

        return changed.Project.Solution;
    }
}
