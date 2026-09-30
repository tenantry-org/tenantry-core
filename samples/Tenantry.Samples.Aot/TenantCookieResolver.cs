using Tenantry.AspNetCore;

namespace Tenantry.Samples.Aot;

/// <summary>
/// Resolves the tenant from a <c>tenant</c> cookie, for a browser that chose a tenant earlier. Registered with
/// <c>tenant.UseResolver&lt;TenantCookieResolver&gt;()</c>, after the header and subdomain resolvers.
/// </summary>
public sealed class TenantCookieResolver : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(context.Request.Cookies.TryGetValue("tenant", out var tenant) && !string.IsNullOrWhiteSpace(tenant)
            ? tenant
            : null);
}
