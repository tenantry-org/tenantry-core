using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from a query string parameter (e.g. <c>?tenantId=acme</c>).
/// </summary>
/// <remarks>
/// Intended for local development and testing convenience only.
/// Do not enable in production — query string parameters are logged and may appear
/// in analytics, CDN caches, and browser history.
/// </remarks>
public sealed class QueryStringTenantResolver(string parameterName = "tenantId") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var value = context.Request.Query[parameterName].FirstOrDefault();
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
