namespace Tenantry.Internal;

/// <summary>
/// The tenant key type <c>AddTenantry</c> was first called with, registered as <see cref="ITenantKeyType"/>.
/// </summary>
internal sealed class TenantKeyType<TKey> : ITenantKeyType
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public Type Type => typeof(TKey);

    public TResult Accept<TResult>(ITenantKeyTypeVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit<TKey>();
    }
}
