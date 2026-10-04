using Microsoft.CodeAnalysis;

namespace Tenantry.EfCore.Analyzers;

/// <summary>The rules Tenantry.EfCore's analyzers report. docs/analyzers.md has a section for each.</summary>
internal static class Rules
{
    private const string Category = "Tenantry";
    private const string HelpBase = "https://tenantry.dev/docs/core/analyzers#";

    public static readonly DiagnosticDescriptor TenantIdWithoutTenantEntity = new(
        "TNY1001",
        "An entity with a TenantId is not tenant-owned",
        "'{0}' has a TenantId property but does not implement ITenantEntity<TKey>, so every tenant reads and writes all " +
        "its rows; {1}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Only an entity type that implements ITenantEntity<TKey> is filtered and checked by tenant. A " +
                     "TenantId property alone does nothing.",
        helpLinkUri: HelpBase + "tny1001",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor IgnoreQueryFilters = new(
        "TNY1002",
        "A query on a tenant-owned entity ignores the tenant filter",
        "This query on '{0}' ignores the tenant filter, so it reads every tenant's rows, and an ExecuteUpdate or " +
        "ExecuteDelete after it changes them; if it is meant to cross tenants, suppress this warning there and say why",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "IgnoreQueryFilters() removes Tenantry's tenant filter from the query. Code that is meant to work " +
                     "across tenants should be behind an authorization check of its own.",
        helpLinkUri: HelpBase + "tny1002");

    public static readonly DiagnosticDescriptor RawSql = new(
        "TNY1003",
        "Raw SQL is not isolated by tenant",
        "{0} is not isolated by tenant: no filter applies to its SQL and no check sees what it changes; add the tenant " +
        "predicate yourself, or use LINQ or FromSql on a tenant-owned set",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Database.SqlQuery, SqlQueryRaw and the ExecuteSql methods map to no entity type, so Tenantry's " +
                     "query filter and save checks do not apply to them.",
        helpLinkUri: HelpBase + "tny1003");

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
