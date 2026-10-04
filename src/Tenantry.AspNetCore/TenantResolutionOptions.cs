using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// How <c>app.UseTenantry()</c> treats requests: whether they need a tenant, the status code of each rejection, and
/// the events it raises. Configure it with <c>tenant.ConfigureResolution(o =&gt; …)</c> or
/// <c>tenant.RequireTenantByDefault()</c>.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
/// <para>
/// A rejected request gets the status code and, when an <see cref="IProblemDetailsService"/> is registered
/// (<c>builder.Services.AddProblemDetails()</c>), a problem details body; otherwise an empty body. The body never
/// repeats the identifier the request sent. <see cref="OnRejected"/> can write another response.
/// </para>
/// <para>
/// Only an endpoint that needs a tenant rejects a request. On any other endpoint, a request whose identifier names
/// no tenant, or names a tenant it may not use, continues without a tenant.
/// </para>
/// </remarks>
public sealed class TenantResolutionOptions<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
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
    /// The status code when the request's identifier names no tenant: the tenant store's
    /// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/> finds none. Default <c>404 Not Found</c>. When access
    /// validators are configured, <see cref="AccessDeniedStatusCode"/> and its response are used instead, so a caller
    /// cannot tell a tenant that does not exist from one it may not use.
    /// </summary>
    public int TenantNotFoundStatusCode { get; set; } = StatusCodes.Status404NotFound;

    /// <summary>
    /// The status code when the request's tenant is inactive or an access validator refuses it. Default
    /// <c>403 Forbidden</c>.
    /// </summary>
    public int AccessDeniedStatusCode { get; set; } = StatusCodes.Status403Forbidden;

    /// <summary>
    /// Called when a request's tenant is made current, before the rest of the pipeline runs: to add the tenant to
    /// your own telemetry, say. To refuse a tenant, use an access validator.
    /// </summary>
    public Func<TenantResolvedContext<TKey>, Task>? OnResolved { get; set; }

    /// <summary>
    /// Called when an endpoint that needs a tenant rejects a request, before Tenantry writes its response. Call
    /// <see cref="TenantRejectedContext{TKey}.HandleResponse"/> after writing your own response (a redirect, or
    /// an error page), or change <see cref="TenantRejectedContext{TKey}.StatusCode"/> and leave the response to
    /// Tenantry.
    /// </summary>
    public Func<TenantRejectedContext<TKey>, Task>? OnRejected { get; set; }
}
