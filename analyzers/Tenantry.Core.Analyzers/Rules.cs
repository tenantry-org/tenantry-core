using Microsoft.CodeAnalysis;

namespace Tenantry.Core.Analyzers;

/// <summary>The rules Tenantry.Core's analyzers report. docs/analyzers.md has a section for each.</summary>
internal static class Rules
{
    private const string Category = "Tenantry";
    private const string HelpBase = "https://tenantry.dev/docs/core/analyzers#";

    public static readonly DiagnosticDescriptor InlineTenantDescriptor = new(
        "TNY3001",
        "A tenant made current from a descriptor built in place",
        "{0} trusts the tenant it is given and does not look it up in the store or check that it is active; pass a " +
        "tenant read from the store, or run the work with RunInScopeAsync(tenantId, ...)",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "MakeCurrent and CreateScope make any descriptor current. One built from an id that came from " +
                     "outside the application can name a tenant that does not exist or is suspended.",
        helpLinkUri: HelpBase + "tny3001");

    public static readonly DiagnosticDescriptor BlockingRunInScope = new(
        "TNY3002",
        "Blocking on RunInScopeAsync",
        "Await RunInScopeAsync instead of blocking on it: it returns to the caller's synchronization context to start " +
        "the work, so blocking there, as on a desktop app's UI thread, can deadlock",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "RunInScopeAsync starts the work and disposes the scope's services on the caller's " +
                     "synchronization context.",
        helpLinkUri: HelpBase + "tny3002");
}
