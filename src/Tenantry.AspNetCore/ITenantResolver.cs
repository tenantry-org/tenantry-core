using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Reads a tenant identifier from an HTTP request: the tenant's id, or a name the tenant store maps to a tenant, such
/// as a subdomain or a host name (see <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/>).
/// Multiple resolvers can be registered; the middleware tries them in registration order
/// and uses the first identifier one returns.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Attempts to read a tenant identifier from the current request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The identifier, or <see langword="null"/> (or an empty string or whitespace) if this resolver cannot determine
    /// the tenant from the current request. Return it as the request carries it: the tenant store finds the tenant it names.
    /// </returns>
    ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default);
}
