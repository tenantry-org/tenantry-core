namespace Tenantry.Internal;

/// <summary>
/// The default <see cref="ITenantActivity{TKey}"/>: asks every registered validator, in registration order.
/// </summary>
internal sealed class TenantActivity<TKey>(IEnumerable<ITenantActivityValidator<TKey>> validators) : ITenantActivity<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ITenantActivityValidator<TKey>[] _validators = [.. validators];

    public async ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        foreach (var validator in _validators)
        {
            if (!await validator.IsActiveAsync(tenant, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public async ValueTask ThrowIfInactiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync(tenant, cancellationToken))
        {
            throw new TenantInactiveException(TenantIds.Format(tenant.TenantId));
        }
    }
}

/// <summary>An <see cref="ITenantActivityValidator{TKey}"/> over a delegate.</summary>
internal sealed class DelegateTenantActivityValidator<TKey>(
    Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> isActive) : ITenantActivityValidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken) =>
        isActive(tenant, cancellationToken);
}
