namespace Tenantry.Http.Internal;

/// <summary>
/// The current tenant's id as <see cref="TenantPropagation.HeaderName"/> carries it, for the handler, which has no
/// key type: <c>AddHttpPropagation()</c> registers it for the application's.
/// </summary>
internal interface ITenantHeaderSource
{
    /// <summary>The current tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>, or null with no tenant.</summary>
    string? CurrentTenantId { get; }
}

internal sealed class TenantHeaderSource<TKey>(ITenantContext<TKey> tenantContext) : ITenantHeaderSource
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public string? CurrentTenantId => tenantContext.CurrentTenant is { } tenant ? TenantIds.Format(tenant.TenantId) : null;
}
