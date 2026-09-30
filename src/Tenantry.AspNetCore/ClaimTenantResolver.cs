using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a claim on the current request principal.
/// </summary>
/// <param name="claimType">The type of the claim that carries the tenant identifier.</param>
public sealed class ClaimTenantResolver(string claimType = "tenant_id") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var value = context.User.FindFirst(claimType)?.Value;
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
