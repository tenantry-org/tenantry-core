using Microsoft.AspNetCore.Http;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Internal;

internal sealed class TenantResolutionOptions<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public bool RequireTenantByDefault { get; set; }
    public int MissingTenantStatusCode { get; set; } = StatusCodes.Status400BadRequest;
    public int AccessDeniedStatusCode { get; set; } = StatusCodes.Status403Forbidden;
    public List<Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>> AccessValidators { get; } = [];
}
