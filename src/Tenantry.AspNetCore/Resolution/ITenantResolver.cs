using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Resolution;

/// <summary>
/// Extracts a tenant identifier from an HTTP request.
/// Multiple resolvers can be registered; the middleware tries them in priority order
/// and uses the first non-null result.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Attempts to extract a tenant ID from the current request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved tenant ID string, or <c>null</c> if this resolver cannot
    /// determine the tenant from the current request.
    /// </returns>
    ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default);
}
