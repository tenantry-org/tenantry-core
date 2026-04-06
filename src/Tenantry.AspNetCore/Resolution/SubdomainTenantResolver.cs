using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from the first subdomain segment of the request host.
/// For example, <c>acme.app.example.com</c> resolves to <c>acme</c>.
/// </summary>
/// <remarks>
/// Requires the host to have at least three dot-separated segments to distinguish
/// a true subdomain (e.g. <c>acme.app.com</c>) from a plain domain (e.g. <c>app.com</c>).
/// Hosts with fewer than three segments (including <c>localhost</c> and <c>acme.localhost</c>)
/// return <c>null</c>. For local development, use header-based resolution instead:
/// <c>builder.ResolveFromHeader("X-Tenant-Id")</c>.
/// </remarks>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var host = context.Request.Host.Host;
        var parts = host.Split('.');

        if (parts.Length < 3)
        {
            return new ValueTask<string?>((string?)null);
        }

        var subdomain = parts[0];
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(subdomain) ? null : subdomain);
    }
}
