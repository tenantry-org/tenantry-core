namespace Tenantry.Internal;

/// <summary>
/// The tenant ids Tenantry reserves for "no tenant".
/// </summary>
internal static class TenantIds
{
    /// <summary>
    /// Returns true when <paramref name="tenantId"/> is <see langword="null"/>, the key type's default
    /// (<see cref="Guid.Empty"/>, <c>0</c>) or an empty string: <see cref="ITenantContext{TKey}.CurrentTenantId"/>
    /// has that value when no tenant is current, so no tenant can have it.
    /// </summary>
    public static bool IsUnset<TKey>(TKey? tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        tenantId is null or string { Length: 0 } || EqualityComparer<TKey>.Default.Equals(tenantId, default!);

    public static void ThrowIfUnset<TKey>(ITenantDescriptor<TKey> tenant, string paramName)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (IsUnset(tenant.TenantId))
        {
            throw new ArgumentException(
                $"Tenant '{tenant.Name}' has the id '{tenant.TenantId}', the default value of {typeof(TKey).Name}, which " +
                "Tenantry reserves for \"no tenant\". Give every tenant an id other than the default (Guid.Empty, 0) " +
                "or an empty string.",
                paramName);
        }
    }
}
