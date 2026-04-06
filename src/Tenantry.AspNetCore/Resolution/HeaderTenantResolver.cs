using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from a request header (e.g. <c>X-Tenant-Id</c>).
/// </summary>
public sealed class HeaderTenantResolver(string headerName) : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var value = context.Request.Headers[headerName].FirstOrDefault();
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
