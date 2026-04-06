namespace Tenantry.Core;

/// <summary>
/// Default implementation of <see cref="ITenantDescriptor{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
public class TenantDescriptor<TKey> : ITenantDescriptor<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    public required TKey TenantId { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }
}
