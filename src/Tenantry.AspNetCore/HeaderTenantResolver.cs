using Microsoft.AspNetCore.Http;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a request header (e.g. <c>X-Tenant-Id</c>).
/// </summary>
/// <remarks>
/// The value is trimmed, and an empty one names no tenant. A header sent more than once names no tenant. A proxy that
/// sets the header must replace one the client sent, not add another.
/// </remarks>
/// <param name="headerName">The name of the header that carries the tenant identifier.</param>
public sealed class HeaderTenantResolver(string headerName) : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        RequestValues.Single(context.Request.Headers[headerName]);
}
