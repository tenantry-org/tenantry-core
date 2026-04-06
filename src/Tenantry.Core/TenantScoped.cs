namespace Tenantry.Core;

/// <summary>
/// Optional base class for tenant-owned objects.
/// Implements <see cref="ITenantScoped{TKey}"/> for convenience.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Using this base class is not required — you can implement <see cref="ITenantScoped{TKey}"/>
/// directly. However, if you do use it, the <c>TenantId</c> property will be automatically implemented.
/// </remarks>
public abstract class TenantScoped<TKey> : ITenantScoped<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    public TKey TenantId { get; set; } = default!;
}
