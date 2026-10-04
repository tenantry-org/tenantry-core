namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Keys ASP.NET Core sets when authorization is added to the pipeline and when it runs. Neither is documented: the
/// tests pin both, so a version of ASP.NET Core that changes them fails the build rather than the order checks.
/// </summary>
internal static class AuthorizationMarkers
{
    /// <summary>
    /// The application builder property <c>app.UseAuthorization()</c> sets, which <c>WebApplication</c> reads to add
    /// its own authorization middleware only when the application did not.
    /// </summary>
    public const string MiddlewareAdded = "__AuthorizationMiddlewareSet";

    /// <summary>
    /// The <c>HttpContext.Items</c> key the authorization middleware sets when it runs for an endpoint, which the
    /// endpoint middleware reads to refuse an endpoint with authorization metadata that authorization never saw.
    /// </summary>
    public const string MiddlewareRan = "__AuthorizationMiddlewareWithEndpointInvoked";
}
