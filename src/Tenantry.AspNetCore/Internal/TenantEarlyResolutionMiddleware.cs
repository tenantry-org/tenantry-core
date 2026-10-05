using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// <c>app.UseTenantResolution()</c>: resolves the request's tenant before the authentication middleware and makes it
/// current, so authentication handlers read the tenant's options. <c>app.UseTenantry()</c>, after authentication, runs the
/// access validators and the claim resolvers, and rejects or continues as it does alone.
/// </summary>
/// <remarks>
/// Until <c>app.UseTenantry()</c> runs, the tenant is current but not yet checked against the user. The request's endpoint
/// is replaced by one that refuses to run (500, event 1011) unless <c>app.UseTenantry()</c> ran for the request first, so
/// a pipeline that skips it fails closed.
/// </remarks>
internal sealed class TenantEarlyResolutionMiddleware<TKey>(
    RequestDelegate next,
    TenantRequestResolution<TKey> resolution,
    ITenantContextSetter<TKey> tenantContext,
    ILoggerFactory loggerFactory)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(TenantResolutionLog.Category);
    private int _warnedAfterAuthentication;
    private int _warnedBeforeRouting;

    public async Task InvokeAsync(HttpContext context)
    {
        // Already handled: app.UseTenantry() ran first, or the request is re-executed (an exception handler, say).
        if (context.Features.Get<TenantResolutionFeature>() is not null ||
            context.Features.Get<EarlyTenantResolution<TKey>>() is not null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.Features.Get<IAuthenticationFeature>() is not null &&
            Interlocked.Exchange(ref _warnedAfterAuthentication, 1) == 0)
        {
            TenantResolutionLog.TenantResolutionAfterAuthentication(_logger, context.Request.Method, context.Request.Path);
        }

        var resolved = await resolution.ResolveAsync(context, beforeAuthentication: true).ConfigureAwait(false);
        EarlyTenantResolution<TKey> early = new(resolved)
        {
            AuthorizedBefore = context.Items.ContainsKey(AuthorizationMarkers.MiddlewareRan),
        };
        context.Features.Set(early);

        // Registered before authentication, so it runs after the callbacks authentication handlers register (the last
        // registered runs first): a cookie handler renews its cookie in one. A request refused for a user signed in
        // under a tenant it may not use must not take that user away in a cookie, so every cookie set since is removed.
        context.Response.OnStarting(
            static state =>
            {
                var (http, refused, cookies) = ((HttpContext, EarlyTenantResolution<TKey>, StringValues))state;

                if (refused.RefusedSignedIn)
                {
                    http.Response.Headers.SetCookie = cookies;
                }

                return Task.CompletedTask;
            },
            (context, early, context.Response.Headers.SetCookie));

        var previous = context.Features.Get<IEndpointFeature>();
        ReplacingEndpointFeature guarded = new(previous, routed => Guard(routed, early));
        context.Features.Set<IEndpointFeature>(guarded);

        // Routing ran first: guard the endpoint it chose.
        if (previous?.Endpoint is { } chosen)
        {
            guarded.Endpoint = chosen;
        }

        using var current = early.Resolution is { Result: ResolutionResult.Resolved, Tenant: { } tenant }
            ? tenantContext.MakeCurrent(tenant)
            : null;

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Features.Set(previous);

            if (previous is null)
            {
                context.SetEndpoint(guarded.Endpoint);
            }
        }

        // Routing ran after this middleware, so a route-value resolver read nothing before authentication.
        if (resolved.MissedResolvers is not null &&
            Volatile.Read(ref _warnedBeforeRouting) == 0 &&
            context.GetEndpoint() is { } endpoint &&
            await resolved.ResolvesNowAsync<RouteValueTenantResolver>(context).ConfigureAwait(false) &&
            Interlocked.Exchange(ref _warnedBeforeRouting, 1) == 0)
        {
            TenantResolutionLog.TenantResolutionBeforeRouting(_logger, endpoint.DisplayName ?? "(unnamed endpoint)");
        }
    }

    private Endpoint? Guard(Endpoint endpoint, EarlyTenantResolution<TKey> early)
    {
        if (endpoint.RequestDelegate is not { } run)
        {
            return null;
        }

        return ReplacingEndpointFeature.WithDelegate(endpoint, context =>
        {
            if (early.Completed)
            {
                return run(context);
            }

            TenantResolutionLog.TenantryDidNotRun(_logger, context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });
    }
}
