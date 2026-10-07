using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a claim on the current request principal. A principal with more than one claim of the type
/// resolves no tenant, so the next resolver runs.
/// </summary>
/// <param name="claimType">The type of the claim that carries the tenant identifier.</param>
public sealed class ClaimTenantResolver(string claimType = "tenant_id") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // A token that lists several tenants names none of them as the one to use.
        var value = context.User.FindAll(claimType).Take(2).ToArray() is [var claim] ? claim.Value : null;
        return new ValueTask<string?>(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
