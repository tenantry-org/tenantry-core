using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore.Internal;

internal static class ClaimTenantAccessValidator
{
    public static ValueTask<bool> ValidateAsync<TKey>(
        HttpContext httpContext,
        ITenantDescriptor<TKey> tenant,
        string claimType)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        return ValueTask.FromResult(ClaimTenantIds.Read(httpContext.User, claimType)
            .Any(id => TenantIds.TryParse<TKey>(id, out var tenantId) &&
                       EqualityComparer<TKey>.Default.Equals(tenantId, tenant.TenantId)));
    }
}
