using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Reads the tenant that owns a tracked <see cref="ITenantEntity{TKey}"/> entry.
/// </summary>
/// <remarks>
/// The original <c>TenantId</c> value is the tenant the row is matched on when EF Core issues an
/// <c>UPDATE</c> or <c>DELETE</c>, because <c>UseTenantry()</c> makes <c>TenantId</c> a concurrency
/// token. For a detached entity passed to <c>Attach</c>, <c>Update</c> or <c>Remove</c>, EF Core copies the
/// supplied value into the original value, so a forged <c>TenantId</c> passes the in-memory check and is
/// then rejected by the database predicate, which matches no row.
/// </remarks>
internal static class TenantOwnership
{
    public const string TenantIdProperty = nameof(ITenantEntity<>.TenantId);

    /// <summary>
    /// Returns the tenant the entry was loaded or attached with (the value EF Core uses in the
    /// <c>WHERE</c> clause), or <see langword="default"/> when none is recorded.
    /// </summary>
    public static TKey? OriginalTenantId<TKey>(EntityEntry entry)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        entry.Property(TenantIdProperty).OriginalValue is TKey original ? original : default;

    /// <summary>
    /// Returns true when <paramref name="tenantId"/> is set and equals <paramref name="currentTenantId"/>.
    /// A <see langword="null"/> value is never owned by the current tenant.
    /// </summary>
    public static bool IsOwnedBy<TKey>(TKey? tenantId, TKey currentTenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        tenantId is not null && EqualityComparer<TKey>.Default.Equals(tenantId, currentTenantId);
}
