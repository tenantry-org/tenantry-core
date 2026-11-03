using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a route value (e.g. <c>/api/{tenant}/resource</c>).
/// </summary>
/// <remarks>
/// The value is trimmed, as a header's and a query parameter's are, and an empty one names no tenant.
/// </remarks>
/// <param name="routeValueKey">The name of the route value that carries the tenant identifier.</param>
public sealed class RouteValueTenantResolver(string routeValueKey = "tenant") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Trimmed, so " 42" and "42" are one identifier, and one entry in CacheTenants' cache.
        var tenantId = context.GetRouteValue(routeValueKey)?.ToString()?.Trim();
        return new ValueTask<string?>(string.IsNullOrEmpty(tenantId) ? null : tenantId);
    }
}
