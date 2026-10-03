namespace Tenantry.EfCore.Internal;

/// <summary>
/// The messages of the <c>RequiresUnreferencedCode</c> and <c>RequiresDynamicCode</c> attributes on every public
/// Tenantry.EfCore method that configures or creates an EF Core context or model. EF Core's own are on its
/// <c>DbContext</c>, and Tenantry builds expression trees for entity types known only at run time.
/// </summary>
internal static class EfCoreRequirements
{
    public const string UnreferencedCode =
        "EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See " +
        "https://aka.ms/efcore-docs-trimming.";

    public const string DynamicCode =
        "EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.";
}
