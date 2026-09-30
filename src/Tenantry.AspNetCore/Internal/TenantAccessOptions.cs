using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// The access validators added with <c>ValidateTenantAccess</c> and <c>ValidateTenantAccessByClaim</c>, in the
/// order they were added.
/// </summary>
internal sealed class TenantAccessOptions<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public List<Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>> Validators { get; } = [];
}
