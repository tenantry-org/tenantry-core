using Microsoft.AspNetCore.Http;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a query string parameter (e.g. <c>?tenantId=acme</c>).
/// </summary>
/// <param name="parameterName">The name of the query string parameter that carries the tenant identifier.</param>
/// <remarks>
/// A parameter given more than once names no tenant. For local development and tests only. Do not use it in
/// production: query string parameters are logged, and appear in analytics, CDN caches and browser history.
/// </remarks>
public sealed class QueryStringTenantResolver(string parameterName = "tenantId") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        RequestValues.Single(context.Request.Query[parameterName]);
}
