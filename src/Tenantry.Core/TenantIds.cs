using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tenantry;

/// <summary>
/// Tenant ids as text, and the ids Tenantry reserves for "no tenant". Tenantry formats and parses tenant ids this way
/// wherever they leave or enter the process: in log scopes and traces, in the headers Tenantry.Http and Tenantry.Pro's
/// jobs and messages carry, and in the identifiers <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/> reads by
/// default.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class TenantIds
{
    /// <summary>
    /// Formats a tenant id with the invariant culture, so it reads the same on every machine: <c>42</c>, a GUID's
    /// <c>D</c> format, or a string as it is.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="tenantId">The tenant id.</param>
    /// <returns>The id as text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    public static string Format<TKey>(TKey tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (tenantId is null)
            throw new ArgumentNullException(nameof(tenantId));

        return tenantId is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : tenantId.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Parses <paramref name="text"/> as a tenant id with the invariant culture, as <see cref="Format{TKey}"/> writes
    /// it. Returns false for text that does not parse and for the ids Tenantry reserves for "no tenant"
    /// (<see cref="IsReserved{TKey}"/>), so a parsed id can name a tenant.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="text">The text, from a header, a route value or a claim.</param>
    /// <param name="tenantId">The parsed id, when this returns true.</param>
    /// <returns>Whether <paramref name="text"/> is a tenant id.</returns>
    public static bool TryParse<TKey>([NotNullWhen(true)] string? text, [MaybeNullWhen(false)] out TKey tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (TKey.TryParse(text, CultureInfo.InvariantCulture, out var parsed) && !IsReserved(parsed))
        {
            tenantId = parsed;
            return true;
        }

        tenantId = default;
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="tenantId"/> is <see langword="null"/>, the key type's default
    /// (<see cref="Guid.Empty"/>, <c>0</c>) or an empty string. <see cref="ITenantContext{TKey}.CurrentTenantId"/> has
    /// that value when no tenant is current, so no tenant can have it, and an entity whose <c>TenantId</c> has it
    /// belongs to no tenant yet.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="tenantId">The tenant id.</param>
    /// <returns>Whether the id is one Tenantry reserves for "no tenant".</returns>
    public static bool IsReserved<TKey>([NotNullWhen(false)] TKey? tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        tenantId is null or string { Length: 0 } || EqualityComparer<TKey>.Default.Equals(tenantId, default!);

    internal static void ThrowIfReserved<TKey>(ITenantDescriptor<TKey> tenant, string paramName)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (IsReserved(tenant.TenantId))
        {
            throw new ArgumentException(
                $"Tenant '{tenant.Name}' has the id '{tenant.TenantId}', the default value of {typeof(TKey).Name}, which " +
                "Tenantry reserves for \"no tenant\". Give every tenant an id other than the default (Guid.Empty, 0) " +
                "or an empty string.",
                paramName);
        }
    }
}
