using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a route value (e.g. <c>/api/{tenant}/resource</c>).
/// </summary>
/// <param name="routeValueKey">The name of the route value that carries the tenant identifier.</param>
public sealed class RouteValueTenantResolver(string routeValueKey = "tenant") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var value = context.GetRouteValue(routeValueKey);
        var tenantId = value?.ToString();
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(tenantId) ? null : tenantId);
    }
}
