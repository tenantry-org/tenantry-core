using Microsoft.AspNetCore.Http;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore;

/// <summary>
/// Resolves the tenant from a claim on the current request principal. A user whose claims of the type list more than
/// one tenant, as repeated claims or a JSON array, resolves no tenant here, and the next resolver runs.
/// </summary>
/// <param name="claimType">The type of the claim that carries the tenant identifier.</param>
public sealed class ClaimTenantResolver(string claimType = "tenant_id") : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // A token that lists several tenants names none of them as the one to use.
        var ids = ClaimTenantIds.Read(context.User, claimType).Take(2).ToArray();
        return new ValueTask<string?>(ids is [var id] ? id : null);
    }
}
