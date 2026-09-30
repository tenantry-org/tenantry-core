using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// How <c>app.UseTenantry()</c> treats requests: whether they need a tenant, and the status code of each
/// rejection. Configure it with <c>tenant.ConfigureResolution(o =&gt; …)</c> or
/// <c>tenant.RequireTenantByDefault()</c>.
/// </summary>
/// <remarks>
/// <para>
/// A rejected request gets the status code and, when an <see cref="IProblemDetailsService"/> is registered
/// (<c>builder.Services.AddProblemDetails()</c>), a problem details body; otherwise an empty body. The body never
/// repeats the identifier the request sent.
/// </para>
/// <para>
/// Only an endpoint that needs a tenant rejects a request. On any other endpoint, a request whose identifier is
/// not valid, names no tenant, or names a tenant it may not use continues without a tenant.
/// </para>
/// </remarks>
public sealed class TenantResolutionOptions
{
    /// <summary>
    /// Whether an endpoint without <c>RequireTenant()</c> or <c>AllowMissingTenant()</c> needs a tenant. Default
    /// <see langword="false"/>.
    /// </summary>
    public bool RequireTenantByDefault { get; set; }

    /// <summary>
    /// The status code when the request does not identify a tenant. Default <c>400 Bad Request</c>.
    /// </summary>
    public int MissingTenantStatusCode { get; set; } = StatusCodes.Status400BadRequest;

    /// <summary>
    /// The status code when the request's identifier cannot be a tenant id: it does not parse as the tenant key
    /// type, or it is the key type's default value. Default <c>400 Bad Request</c>.
    /// </summary>
    public int InvalidTenantStatusCode { get; set; } = StatusCodes.Status400BadRequest;

    /// <summary>
    /// The status code when the tenant store has no tenant with the request's identifier. Default
    /// <c>404 Not Found</c>. When access validators are configured, <see cref="AccessDeniedStatusCode"/> and its
    /// response are used instead, so a caller cannot tell a tenant that does not exist from one it may not use.
    /// </summary>
    public int TenantNotFoundStatusCode { get; set; } = StatusCodes.Status404NotFound;

    /// <summary>
    /// The status code when an access validator refuses the request's tenant. Default <c>403 Forbidden</c>.
    /// </summary>
    public int AccessDeniedStatusCode { get; set; } = StatusCodes.Status403Forbidden;
}
