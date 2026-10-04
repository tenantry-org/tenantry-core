using Microsoft.Extensions.Logging;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// The middleware's log messages, under the category <c>Tenantry.AspNetCore</c>. Their event ids are stable: the
/// documentation lists them, so applications can alert on them.
/// </summary>
internal static partial class TenantResolutionLog
{
    public const string Category = TenantryAspNetCoreTelemetry.LogCategory;

    [LoggerMessage(1001, LogLevel.Debug, "Tenant {TenantId} resolved for {Method} {Path}", EventName = "TenantResolved")]
    public static partial void TenantResolved(ILogger logger, string tenantId, string method, string path);

    [LoggerMessage(1002, LogLevel.Debug,
        "Request {Method} {Path} identifies no tenant. Its endpoint does not require one, so it continues without one",
        EventName = "NoTenantIdentifier")]
    public static partial void NoTenantIdentifier(ILogger logger, string method, string path);

    [LoggerMessage(1003, LogLevel.Warning,
        "Request {Method} {Path} identifies no tenant, and its endpoint requires one. Returning {StatusCode}",
        EventName = "TenantRequired")]
    public static partial void TenantRequired(ILogger logger, string method, string path, int statusCode);

    [LoggerMessage(1004, LogLevel.Warning,
        "The identifier '{Identifier}' of request {Method} {Path} names no tenant, and its endpoint requires one. " +
        "Returning {StatusCode}",
        EventName = "TenantNotFound")]
    public static partial void TenantNotFound(ILogger logger, string identifier, string method, string path, int statusCode);

    [LoggerMessage(1005, LogLevel.Warning,
        "Request {Method} {Path} by user '{User}' may not use tenant {TenantId}: an access validator refused it",
        EventName = "TenantAccessDenied")]
    public static partial void TenantAccessDenied(ILogger logger, string method, string path, string user, string tenantId);

    [LoggerMessage(1012, LogLevel.Warning,
        "Request {Method} {Path} by user '{User}' names tenant {TenantId}, which is not active",
        EventName = "TenantInactive")]
    public static partial void TenantInactive(ILogger logger, string method, string path, string user, string tenantId);

    [LoggerMessage(1006, LogLevel.Debug,
        "The tenant identifier of request {Method} {Path} {Reason}. Its endpoint does not require a tenant, so it " +
        "continues without one",
        EventName = "ContinuingWithoutTenant")]
    public static partial void ContinuingWithoutTenant(ILogger logger, string method, string path, string reason);

    [LoggerMessage(1007, LogLevel.Warning,
        "app.UseTenantry() ran before routing chose {Endpoint}. A request without a tenant to an endpoint that " +
        "requires one is still rejected, but RequireTenantByDefault overrides AllowMissingTenant. Call " +
        "app.UseRouting() before app.UseTenantry(). Logged once",
        EventName = "TenantryBeforeRouting")]
    public static partial void TenantryBeforeRouting(ILogger logger, string endpoint);

    [LoggerMessage(1008, LogLevel.Warning,
        "The authentication middleware ran after app.UseTenantry() for request {Method} {Path}, so ResolveFromClaim " +
        "did not see its user's claims. Call app.UseAuthentication() before app.UseTenantry(). Logged once",
        EventName = "TenantryBeforeAuthentication")]
    public static partial void TenantryBeforeAuthentication(ILogger logger, string method, string path);

    [LoggerMessage(1009, LogLevel.Warning,
        "The output cache ran before app.UseTenantry() for request {Method} {Path}, so its response was not cached: " +
        "IsolateOutputCache() caches only responses for requests app.UseTenantry() handled. Call app.UseTenantry() " +
        "before app.UseOutputCache(). Logged once",
        EventName = "OutputCacheBeforeTenantry")]
    public static partial void OutputCacheBeforeTenantry(ILogger logger, string method, string path);

    [LoggerMessage(1010, LogLevel.Warning,
        "app.UseTenantResolution() ran after the authentication middleware for request {Method} {Path}, so " +
        "authentication used the options of no tenant. Call app.UseTenantResolution() before app.UseAuthentication(). " +
        "Logged once",
        EventName = "TenantResolutionAfterAuthentication")]
    public static partial void TenantResolutionAfterAuthentication(ILogger logger, string method, string path);

    [LoggerMessage(1011, LogLevel.Error,
        "app.UseTenantResolution() resolved request {Method} {Path}, but app.UseTenantry() did not run before its " +
        "endpoint, so the access validators never checked the tenant. The endpoint was not run (500). Call " +
        "app.UseTenantry() after app.UseAuthentication(), in every branch",
        EventName = "TenantryDidNotRun")]
    public static partial void TenantryDidNotRun(ILogger logger, string method, string path);

    [LoggerMessage(1013, LogLevel.Error,
        "The authorization middleware ran for request {Method} {Path} after app.UseTenantResolution() and before " +
        "app.UseTenantry(), so it saw a tenant the access validators had not checked. The request was refused (500). " +
        "Call app.UseAuthorization() after app.UseTenantry()",
        EventName = "AuthorizationBeforeTenantry")]
    public static partial void AuthorizationBeforeTenantry(ILogger logger, string method, string path);

    [LoggerMessage(1014, LogLevel.Warning,
        "This version of ASP.NET Core does not set {Keys}, which Tenantry reads to refuse authorization between " +
        "app.UseTenantResolution() and app.UseTenantry(), so that order is no longer caught. Put only " +
        "app.UseAuthentication() between the two, and report this to Tenantry",
        EventName = "AuthorizationMarkersMissing")]
    public static partial void AuthorizationMarkersMissing(ILogger logger, string keys);

    [LoggerMessage(1015, LogLevel.Error,
        "Signing out scheme {Scheme} failed while refusing request {Method} {Path}, whose user was signed in under a " +
        "tenant the access validators refused. A session that scheme keeps may still hold that user. The request was " +
        "refused as usual",
        EventName = "SignOutFailed")]
    public static partial void SignOutFailed(
        ILogger logger, Exception exception, string scheme, string method, string path);
}
