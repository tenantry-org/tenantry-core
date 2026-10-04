namespace Tenantry.EfCore;

/// <summary>
/// The names of the query filters Tenantry adds or names on EF Core 10 and later.
/// </summary>
/// <remarks>
/// EF Core 8 and 9 have no named filters: there the tenant filter is merged into the entity's own filter, and only
/// <c>IgnoreQueryFilters()</c> removes it, together with your own.
/// </remarks>
public static class TenantryQueryFilters
{
    /// <summary>
    /// The name of the tenant query filter <c>UseTenantry()</c> adds on EF Core 10 and later. Pass it to
    /// <c>IgnoreQueryFilters([TenantryQueryFilters.Tenant])</c> to read every tenant's rows while keeping your other
    /// filters, such as a soft-delete filter.
    /// </summary>
    public const string Tenant = "Tenantry.Tenant";

    /// <summary>
    /// The name an entity's unnamed query filter gets on EF Core 10 and later. Your filter still applies to every
    /// query; pass this name to <c>IgnoreQueryFilters</c> to remove it alone, or name your filters yourself.
    /// </summary>
    /// <remarks>
    /// EF Core does not allow a named filter beside an unnamed one, so <c>UseTenantry()</c> names yours, to add the
    /// tenant filter beside it rather than merge the two.
    /// </remarks>
    public const string Application = "Tenantry.Application";
}
