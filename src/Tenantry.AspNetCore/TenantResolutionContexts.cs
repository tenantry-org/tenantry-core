using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// The request whose tenant <c>app.UseTenantry()</c> made current, passed to
/// <see cref="TenantResolutionOptions{TKey}.OnResolved"/>.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public sealed class TenantResolvedContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    internal TenantResolvedContext(HttpContext httpContext, ITenantDescriptor<TKey> tenant)
    {
        HttpContext = httpContext;
        Tenant = tenant;
    }

    /// <summary>The request.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The request's tenant, now current.</summary>
    public ITenantDescriptor<TKey> Tenant { get; }
}

/// <summary>
/// A request that <c>app.UseTenantry()</c> rejects, passed to <see cref="TenantResolutionOptions{TKey}.OnRejected"/>.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
/// <see cref="Reason"/> is the actual reason, for your logs. With access validators, a request whose tenant does not
/// exist gets the response of one whose tenant it may not use (<see cref="StatusCode"/> is the access-denied status),
/// so a caller cannot find out which tenants exist: keep that in a response of your own.
/// </remarks>
public sealed class TenantRejectedContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    internal TenantRejectedContext(
        HttpContext httpContext,
        TenantRejectionReason reason,
        int statusCode,
        string? identifier,
        ITenantDescriptor<TKey>? tenant)
    {
        HttpContext = httpContext;
        Reason = reason;
        StatusCode = statusCode;
        Identifier = identifier;
        Tenant = tenant;
    }

    /// <summary>The request.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>Why the request is rejected.</summary>
    public TenantRejectionReason Reason { get; }

    /// <summary>
    /// The status code of Tenantry's response, from <see cref="TenantResolutionOptions{TKey}"/>. Change it to send
    /// another.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// The identifier the request sent, or <see langword="null"/> when it sent none. It is request input: do not
    /// repeat it in a response without encoding it.
    /// </summary>
    public string? Identifier { get; }

    /// <summary>
    /// The tenant that was refused, for <see cref="TenantRejectionReason.Inactive"/> and
    /// <see cref="TenantRejectionReason.AccessDenied"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ITenantDescriptor<TKey>? Tenant { get; }

    /// <summary>Whether <see cref="HandleResponse"/> was called.</summary>
    public bool IsHandled { get; private set; }

    /// <summary>
    /// Tells Tenantry that the handler wrote the response, so it writes none.
    /// </summary>
    public void HandleResponse() => IsHandled = true;
}

/// <summary>
/// Why <c>app.UseTenantry()</c> rejects a request to an endpoint that needs a tenant.
/// </summary>
public enum TenantRejectionReason
{
    /// <summary>No resolver found an identifier in the request.</summary>
    Missing,

    /// <summary>The identifier names no tenant.</summary>
    NotFound,

    /// <summary>An access validator refused the tenant.</summary>
    AccessDenied,

    /// <summary>
    /// The tenant is not active: a <c>ValidateTenantActivity</c> check refused it. Only a request the access validators
    /// allow is rejected for this reason.
    /// </summary>
    Inactive,
}
