namespace Tenantry.Internal;

/// <summary>
/// Records the tenant key type <c>AddTenantry</c> was first called with, so a call with another type throws.
/// </summary>
internal sealed record TenantryRegistration(Type KeyType);
