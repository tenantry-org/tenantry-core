using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Keys ASP.NET Core sets when authorization is added to the pipeline and when it runs. Neither is documented: the
/// tests pin both, and <see cref="Missing"/> checks them against the running ASP.NET Core as the application starts.
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

    /// <summary>
    /// The keys, of <paramref name="added"/> and <paramref name="ran"/>, that the running ASP.NET Core does not set, found
    /// by adding authorization to a builder of its own and running the middleware once on a request of its own, with a
    /// policy provider that has no policies. Empty when authorization is not registered, as no order check applies.
    /// </summary>
    public static IReadOnlyList<string> Missing(IServiceProvider services, string added = MiddlewareAdded, string ran = MiddlewareRan)
    {
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(IPolicyEvaluator)) != true)
        {
            return [];
        }

        List<string> missing = [];
        ApplicationBuilder builder = new(services);
        builder.UseAuthorization();

        if (!builder.Properties.ContainsKey(added))
        {
            missing.Add(added);
        }

        DefaultHttpContext context = new() { RequestServices = services };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "Tenantry probe"));
        AuthorizationMiddleware middleware = new(_ => Task.CompletedTask, NoPolicies.Instance);

        // Completes at once with no policy; one that does not is not waited for, and its key is not checked.
        var run = middleware.Invoke(context);

        if (run.IsCompletedSuccessfully && !context.Items.ContainsKey(ran))
        {
            missing.Add(ran);
        }

        return missing;
    }

    private sealed class NoPolicies : IAuthorizationPolicyProvider
    {
        public static NoPolicies Instance { get; } = new();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) => Task.FromResult<AuthorizationPolicy?>(null);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            Task.FromResult(new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build());

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);
    }
}
