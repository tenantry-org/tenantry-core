using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Resolves the current tenant from the HTTP request and makes it current for the rest of the pipeline.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Registered via <c>app.UseTenantry()</c>. Resolvers, created in the request's scope, are tried in registration
/// order, and the first identifier one returns is looked up with <see cref="ITenantLookup{TKey}"/> (through
/// the cache, with <c>CacheTenants</c>). <see cref="ITenantActivity{TKey}"/> must find the tenant active, and the
/// access validators, also from the request's scope, must all allow it. The tenant is then current for the rest of the request, which is tagged <c>tenant.id</c> and logged with a
/// <c>TenantId</c> scope.
/// </para>
/// <para>
/// An endpoint that needs a tenant rejects a request without a usable one, with the status code from
/// <see cref="TenantResolutionOptions{TKey}"/> and, when an <see cref="IProblemDetailsService"/> is registered, a
/// problem details body. Any other endpoint runs without a tenant: a request whose identifier names no tenant, or names
/// one an access validator refuses, is treated as one without an identifier, so it tells a caller nothing about which
/// tenants exist.
/// </para>
/// </remarks>
internal sealed class TenantResolutionMiddleware<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly RequestDelegate _next;
    private readonly ITenantContextSetter<TKey> _tenantContext;
    private readonly TenantResolutionOptions<TKey> _options;
    private readonly TenantResolutionMetrics _metrics;
    private readonly ILogger _logger;
    private readonly TenantRequestResolution<TKey> _resolution;
    private readonly TenantRequestMetricsOptions<TKey> _requestMetrics;
    private int _warnedBeforeRouting;
    private int _warnedBeforeAuthentication;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        TenantRequestResolution<TKey> resolution,
        ITenantContextSetter<TKey> tenantContext,
        IOptions<TenantResolutionOptions<TKey>> options,
        TenantResolutionMetrics metrics,
        IOptions<TenantRequestMetricsOptions<TKey>> requestMetrics,
        ILoggerFactory loggerFactory)
    {
        _next = next;
        _resolution = resolution;
        _tenantContext = tenantContext;
        _options = options.Value;
        _metrics = metrics;
        _requestMetrics = requestMetrics.Value;
        _logger = loggerFactory.CreateLogger(TenantResolutionLog.Category);
    }

    /// <summary>
    /// Attempts to resolve and set the current tenant, then continues the pipeline.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Features.Set(TenantResolutionFeature.Instance);

        // Captured first: the resolution's own activity is the current one while it runs.
        var requestActivity = Activity.Current;
        var endpoint = context.GetEndpoint();
        var required = IsTenantRequired(endpoint);
        var resolution = context.Features.Get<EarlyTenantResolution<TKey>>() is { } early
            ? await CompleteAsync(context, early)
            : await _resolution.ResolveAsync(context, beforeAuthentication: false);

        // Before routing, whether the request is rejected is known only once routing has chosen its endpoint.
        if (endpoint is not null || required || resolution.Result == ResolutionResult.Resolved)
        {
            _metrics.Record(resolution.Result, rejected: required && resolution.Result != ResolutionResult.Resolved);
        }

        if (resolution.Tenant is { } tenant && resolution.Result == ResolutionResult.Resolved)
        {
            var tenantId = TenantIds.Format(tenant.TenantId);
            requestActivity?.SetTag(TenantTelemetry.TenantIdTag, tenantId);

            if (_requestMetrics.Enabled)
            {
                TagRequestMetrics(context, tenant, tenantId);
            }

            using var current = _tenantContext.Use(tenant);
            using var logScope = _logger.BeginScope(TenantTelemetry.CreateLogScope(tenantId));

            TenantResolutionLog.TenantResolved(_logger, tenantId, context.Request.Method, context.Request.Path);

            if (_options.OnResolved is { } onResolved)
            {
                await onResolved(new TenantResolvedContext<TKey>(context, tenant));
            }

            await NextAsync(context, endpoint is null, resolution);
            return;
        }

        // A tenant app.UseTenantResolution() made current, and the access validators then refused, is not current for the
        // rest of the request.
        using var noTenant = _tenantContext.HasTenant ? _tenantContext.UseNoTenant() : null;

        if (resolution is { Result: ResolutionResult.AccessDenied or ResolutionResult.Inactive, Tenant: { } refused })
        {
            var user = context.User.Identity?.Name ?? "(anonymous)";
            var refusedId = TenantIds.Format(refused.TenantId);

            if (resolution.Result == ResolutionResult.Inactive)
            {
                TenantResolutionLog.TenantInactive(_logger, context.Request.Method, context.Request.Path, user, refusedId);
            }
            else
            {
                TenantResolutionLog.TenantAccessDenied(_logger, context.Request.Method, context.Request.Path, user, refusedId);
            }
        }

        if (required)
        {
            await RejectAsync(context, resolution);
            return;
        }

        switch (resolution.Result)
        {
            case ResolutionResult.Missing:
                TenantResolutionLog.NoTenantIdentifier(_logger, context.Request.Method, context.Request.Path);
                break;
            case ResolutionResult.NotFound:
                TenantResolutionLog.ContinuingWithoutTenant(_logger, context.Request.Method, context.Request.Path, "names no tenant");
                break;
            case ResolutionResult.Inactive:
                TenantResolutionLog.ContinuingWithoutTenant(
                    _logger,
                    context.Request.Method,
                    context.Request.Path,
                    "names a tenant that is not active");
                break;
            default:
                TenantResolutionLog.ContinuingWithoutTenant(
                    _logger,
                    context.Request.Method,
                    context.Request.Path,
                    "names a tenant the request may not use");
                break;
        }

        await NextAsync(context, endpoint is null, resolution);
    }

    // Before authentication, app.UseTenantResolution() found what it could: the access validators, and the claim
    // resolvers when nothing else named a tenant, run now that the user is known.
    private async ValueTask<TenantResolution<TKey>> CompleteAsync(HttpContext context, EarlyTenantResolution<TKey> early)
    {
        early.Completed = true;
        var resolution = early.Resolution;

        if (resolution.Result == ResolutionResult.Missing)
        {
            return await _resolution.ResolveAsync(context, beforeAuthentication: false);
        }

        // An inactive tenant is checked too, so a caller the validators refuse is denied access whatever its state.
        if (resolution is { Result: ResolutionResult.Resolved or ResolutionResult.Inactive, Tenant: { } tenant } &&
            !await _resolution.ValidateAsync(context, tenant))
        {
            return resolution with { Result = ResolutionResult.AccessDenied };
        }

        return resolution;
    }

    // Runs the rest of the pipeline, then looks for what the request needed and only had later: an endpoint routing
    // chose after this middleware ran, or a user the authentication middleware signed in after it (it sets
    // IAuthenticationFeature on every request it runs for, so a user signed in later by other code, such as
    // authorization with a scheme that is not the default, is not mistaken for it). Each is logged once.
    // Before routing, a request without a tenant still fails closed: an endpoint routing chooses that requires one is
    // replaced by one that rejects the request.
    private async Task NextAsync(HttpContext context, bool endpointWasNull, TenantResolution<TKey> resolution)
    {
        ReplacingEndpointFeature? routed = null;
        IEndpointFeature? previous = null;

        if (endpointWasNull && resolution.Result != ResolutionResult.Resolved)
        {
            previous = context.Features.Get<IEndpointFeature>();
            routed = new ReplacingEndpointFeature(previous, endpoint => IsTenantRequired(endpoint)
                ? RejectingEndpoint(endpoint, resolution)
                : null);
            context.Features.Set<IEndpointFeature>(routed);
        }

        try
        {
            await _next(context);
        }
        finally
        {
            if (routed is not null)
            {
                context.Features.Set(previous);

                if (previous is null)
                {
                    context.SetEndpoint(routed.Endpoint);
                }

                _metrics.Record(resolution.Result, routed.Replaced);
            }
        }

        if (endpointWasNull &&
            Volatile.Read(ref _warnedBeforeRouting) == 0 &&
            context.GetEndpoint() is { } endpoint &&
            HasTenantMetadata(endpoint) &&
            Interlocked.Exchange(ref _warnedBeforeRouting, 1) == 0)
        {
            TenantResolutionLog.TenantryBeforeRouting(_logger, endpoint.DisplayName ?? "(unnamed endpoint)");
        }

        if (resolution.MissedClaimResolvers is { } claimResolvers &&
            Volatile.Read(ref _warnedBeforeAuthentication) == 0 &&
            context.Features.Get<IAuthenticationFeature>() is not null &&
            context.User.Identity?.IsAuthenticated == true)
        {
            foreach (var claimResolver in claimResolvers)
            {
                if (await claimResolver.ResolveAsync(context, context.RequestAborted) is not null &&
                    Interlocked.Exchange(ref _warnedBeforeAuthentication, 1) == 0)
                {
                    TenantResolutionLog.TenantryBeforeAuthentication(_logger, context.Request.Method, context.Request.Path);
                    break;
                }
            }
        }
    }

    // Keeps the endpoint's metadata, so authorization and the rest of the pipeline treat the request as before.
    private Endpoint RejectingEndpoint(Endpoint endpoint, TenantResolution<TKey> resolution)
    {
        return ReplacingEndpointFeature.WithDelegate(endpoint, context => RejectAsync(context, resolution));
    }

    private static bool HasTenantMetadata(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<RequireTenantAttribute>() is not null ||
        endpoint.Metadata.GetMetadata<AllowMissingTenantAttribute>() is not null;

    private void TagRequestMetrics(HttpContext context, ITenantDescriptor<TKey> tenant, string tenantId)
    {
        // ASP.NET Core adds the feature only while something listens to the metric.
        if (context.Features.Get<IHttpMetricsTagsFeature>() is not { } metrics)
        {
            return;
        }

        var value = _requestMetrics.GetTagValue is { } getTagValue ? getTagValue(tenant) : tenantId;

        // A request the pipeline runs again (UseExceptionHandler("/error"), UseStatusCodePagesWithReExecute) is resolved
        // twice with the same feature.
        if (value is not null && !metrics.Tags.Any(tag => tag.Key == TenantTelemetry.TenantIdTag))
        {
            metrics.Tags.Add(new KeyValuePair<string, object?>(TenantTelemetry.TenantIdTag, value));
        }
    }

    private bool IsTenantRequired(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return _options.RequireTenantByDefault;
        }

        for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
        {
            switch (endpoint.Metadata[i])
            {
                case AllowMissingTenantAttribute:
                    return false;
                case RequireTenantAttribute:
                    return true;
            }
        }

        return _options.RequireTenantByDefault;
    }

    private async Task RejectAsync(HttpContext context, TenantResolution<TKey> resolution)
    {
        var request = context.Request;

        // With access validators, a tenant that does not exist gets the same response as one the caller may not
        // use, so a caller cannot find out which tenants exist. An inactive tenant gets that response too, with its own
        // status code, so only an application that changes the status tells a caller the tenant is suspended.
        var (statusCode, title, detail) = resolution.Result switch
        {
            ResolutionResult.Missing => (_options.MissingTenantStatusCode, "Tenant required",
                "This endpoint requires a tenant, and the request does not identify one."),
            ResolutionResult.NotFound when !_resolution.HasValidators => (_options.TenantNotFoundStatusCode, "Tenant not found",
                "The request's tenant does not exist."),
            _ => (resolution.Result == ResolutionResult.Inactive ? _options.InactiveTenantStatusCode : _options.AccessDeniedStatusCode,
                "Tenant access denied", "The request may not use its tenant."),
        };

        switch (resolution.Result)
        {
            case ResolutionResult.Missing:
                TenantResolutionLog.TenantRequired(_logger, request.Method, request.Path, statusCode);
                break;
            case ResolutionResult.NotFound:
                TenantResolutionLog.TenantNotFound(_logger, resolution.Identifier!, request.Method, request.Path, statusCode);
                break;
        }

        var reason = resolution.Result switch
        {
            ResolutionResult.Missing => TenantRejectionReason.Missing,
            ResolutionResult.NotFound => TenantRejectionReason.NotFound,
            ResolutionResult.Inactive => TenantRejectionReason.Inactive,
            _ => TenantRejectionReason.AccessDenied,
        };

        TenantRejectedContext<TKey> rejected = new(
            context,
            reason,
            statusCode,
            resolution.Identifier,
            resolution.Result is ResolutionResult.AccessDenied or ResolutionResult.Inactive ? resolution.Tenant : null);

        if (_options.OnRejected is { } onRejected)
        {
            await onRejected(rejected);

            if (rejected.IsHandled)
            {
                return;
            }
        }

        context.Response.StatusCode = rejected.StatusCode;

        // Never repeats the identifier the request sent.
        if (context.RequestServices.GetService<IProblemDetailsService>() is { } problemDetails)
        {
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = rejected.StatusCode, Title = title, Detail = detail },
            });
        }
    }
}

/// <summary>
/// The endpoint feature while a Tenantry middleware runs before routing, or needs to change the endpoint: an endpoint
/// set through it is replaced by the one the replacement function gives, if any. It keeps the endpoint in the feature it
/// replaced, when there was one.
/// </summary>
internal sealed class ReplacingEndpointFeature(IEndpointFeature? inner, Func<Endpoint, Endpoint?> replace) : IEndpointFeature
{
    private Endpoint? _endpoint;

    public bool Replaced { get; private set; }

    public Endpoint? Endpoint
    {
        get => inner is null ? _endpoint : inner.Endpoint;
        set
        {
            if (value is not null && replace(value) is { } replacement)
            {
                value = replacement;
                Replaced = true;
            }

            if (inner is null)
            {
                _endpoint = value;
            }
            else
            {
                inner.Endpoint = value;
            }
        }
    }

    /// <summary>The same endpoint, with its metadata, running <paramref name="requestDelegate"/> instead.</summary>
    public static Endpoint WithDelegate(Endpoint endpoint, RequestDelegate requestDelegate) =>
        endpoint is RouteEndpoint route
            ? new RouteEndpoint(requestDelegate, route.RoutePattern, route.Order, route.Metadata, route.DisplayName)
            : new Endpoint(requestDelegate, endpoint.Metadata, endpoint.DisplayName);
}
