using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// An access validator added as a delegate, with <c>ValidateTenantAccess(...)</c> or
/// <c>ValidateTenantAccessByClaim(...)</c>.
/// </summary>
internal sealed class DelegateTenantAccessValidator<TKey>(
    Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validate)
    : ITenantAccessValidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken) =>
        validate(context, tenant, cancellationToken);
}
