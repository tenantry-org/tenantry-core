using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Tenantry.Core;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Reads the tenant that owns a tracked <see cref="ITenantScoped{TKey}"/> entry.
/// </summary>
/// <remarks>
/// The original <c>TenantId</c> value is the tenant the row is matched on when EF Core issues an
/// <c>UPDATE</c> or <c>DELETE</c>, because <c>ApplyTenantFilters</c> marks <c>TenantId</c> as a concurrency
/// token. For a detached entity passed to <c>Attach</c>, <c>Update</c> or <c>Remove</c>, EF Core copies the
/// supplied value into the original value, so a forged <c>TenantId</c> passes the in-memory check and is
/// then rejected by the database predicate, which matches no row.
/// </remarks>
internal static class TenantOwnership
{
    private const string TenantIdProperty = nameof(ITenantScoped<>.TenantId);

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

    /// <summary>
    /// Returns true when <paramref name="tenantId"/> is unset: <see langword="null"/>, the type's default,
    /// or <see cref="string.Empty"/> (string properties are commonly initialised to empty rather than null).
    /// </summary>
    public static bool IsUnstamped<TKey>(TKey? tenantId)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        tenantId is null or string { Length: 0 } || EqualityComparer<TKey>.Default.Equals(tenantId, default!);

    /// <summary>
    /// Returns true when the database enforces the stored tenant on <c>UPDATE</c> and <c>DELETE</c> for
    /// this entry: <c>TenantId</c> is a concurrency token or part of the primary key.
    /// </summary>
    public static bool IsEnforcedByDatabase(EntityEntry entry)
    {
        var property = entry.Property(TenantIdProperty).Metadata;
        return property.IsConcurrencyToken || property.IsPrimaryKey();
    }
}
