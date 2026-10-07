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
/// An endpoint that needs a tenant rejects a request without a usable one. On any other endpoint, a request whose
/// identifier names no tenant, or names a tenant it may not use, continues without a tenant, unless
/// <c>app.UseTenantResolution()</c> made that tenant current during authentication and the request is signed in: then
/// it is rejected with <see cref="AccessDeniedStatusCode"/>.
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
    /// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/> finds none. Default <c>404 Not Found</c>.
    /// </summary>
    /// <remarks>
    /// When access validators are configured, <see cref="AccessDeniedStatusCode"/> and its response are used instead,
    /// so a caller cannot tell a tenant that does not exist from one it may not use.
    /// </remarks>
    public int TenantNotFoundStatusCode { get; set; } = StatusCodes.Status404NotFound;

    /// <summary>
    /// The status code when an access validator refuses the request's tenant. Default <c>403 Forbidden</c>.
    /// </summary>
    public int AccessDeniedStatusCode { get; set; } = StatusCodes.Status403Forbidden;

    /// <summary>
    /// The status code when the request's tenant is not active (<c>ValidateTenantActivity</c>). Default
    /// <c>403 Forbidden</c>.
    /// </summary>
    /// <remarks>
    /// The response is the access-denied one, so by default a caller cannot tell a suspended tenant from one it may
    /// not use. Set another status, such as <c>402 Payment Required</c>, to tell it. The access validators run first,
    /// so a caller they refuse gets <see cref="AccessDeniedStatusCode"/> whether or not the tenant is active.
    /// </remarks>
    public int InactiveTenantStatusCode { get; set; } = StatusCodes.Status403Forbidden;

    /// <summary>
    /// Called when a request's tenant is made current, before the rest of the pipeline runs, for example to add the
    /// tenant to your own telemetry. To refuse a tenant, use an access validator.
    /// </summary>
    /// <remarks>
    /// Changes the handler makes to ambient state, such as <c>CultureInfo.CurrentCulture</c> or an
    /// <see cref="System.Threading.AsyncLocal{T}"/>, reach the rest of the pipeline only if it is not an <c>async</c>
    /// method: set them and return <c>Task.CompletedTask</c>. An <c>async</c> handler's changes are undone when it
    /// returns, even those made before its first <c>await</c>.
    /// </remarks>
    public Func<TenantResolvedContext<TKey>, Task>? OnResolved { get; set; }

    /// <summary>
    /// Called before Tenantry writes a rejection: on an endpoint that needs a tenant, and, with
    /// <c>app.UseTenantResolution()</c>, on any endpoint for a signed-in user whose tenant was current during
    /// authentication and which the access validators refuse. Call
    /// <see cref="TenantRejectedContext{TKey}.HandleResponse"/> after writing your own response (a redirect, or an
    /// error page), or change <see cref="TenantRejectedContext{TKey}.StatusCode"/> and leave the response to Tenantry.
    /// </summary>
    public Func<TenantRejectedContext<TKey>, Task>? OnRejected { get; set; }
}
