using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Reads the tenant another service sent, in <see cref="TenantPropagation.HeaderName"/>. The value is a tenant id, as
/// Tenantry.Http and Tenantry.Pro's integrations send it, so the middleware looks it up by id
/// (<see cref="ITenantLookup{TKey}.GetTenantAsync"/>) rather than as an identifier, which a store may map to slugs only.
/// </summary>
internal sealed class PropagationHeaderTenantResolver : ITenantResolver
{
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var value = context.Request.Headers[TenantPropagation.HeaderName].FirstOrDefault();
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
