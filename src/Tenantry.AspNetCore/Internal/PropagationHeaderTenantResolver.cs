using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Reads the tenant another service sent, in <see cref="TenantPropagation.HeaderName"/>. The value is a tenant id, as
/// Tenantry.Http and Tenantry.Pro's integrations send it, so the middleware looks it up by id
/// (<see cref="ITenantLookup{TKey}.GetTenantAsync"/>) rather than as an identifier, which a store may map to slugs only.
/// The header is read only from a caller the application trusts; it needs the user, so it runs after authentication.
/// </summary>
internal sealed class PropagationHeaderTenantResolver(Func<HttpContext, bool> isTrustedCaller) : ITenantResolver
{
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        context.Request.Headers.TryGetValue(TenantPropagation.HeaderName, out var values) && isTrustedCaller(context)
            ? RequestValues.Single(values)
            : ValueTask.FromResult<string?>(null);
}
