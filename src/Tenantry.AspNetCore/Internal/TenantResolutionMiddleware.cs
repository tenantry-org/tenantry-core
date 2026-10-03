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
    private readonly ITenantLookup<TKey> _tenants;
    private readonly ITenantContextSetter<TKey> _tenantContext;
    private readonly TenantResolutionOptions<TKey> _options;
    private readonly TenantResolutionMetrics _metrics;
    private readonly ILogger _logger;
    private readonly ITenantActivity<TKey>? _activity;
    private readonly bool _hasValidators;
    private int _warnedBeforeRouting;
    private int _warnedBeforeAuthentication;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ITenantLookup<TKey> tenants,
        ITenantContextSetter<TKey> tenantContext,
        IOptions<TenantResolutionOptions<TKey>> options,
        TenantResolutionMetrics metrics,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _next = next;
        _tenants = tenants;
        _tenantContext = tenantContext;
        _options = options.Value;
        _metrics = metrics;
        _logger = loggerFactory.CreateLogger(TenantResolutionLog.Category);
        _activity = services.GetService<ITenantActivity<TKey>>();

        // Without IServiceProviderIsService, validators are assumed to exist: an unknown tenant then gets the
        // access-denied response, which hides which tenants exist either way.
        _hasValidators = services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantAccessValidator<TKey>)) ?? true;
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
        var resolution = await ResolveAsync(context);

        // Before routing, whether the request is rejected is known only once routing has chosen its endpoint.
        if (endpoint is not null || required || resolution.Result == ResolutionResult.Resolved)
        {
            _metrics.Record(resolution.Result, rejected: required && resolution.Result != ResolutionResult.Resolved);
        }

        if (resolution.Tenant is { } tenant && resolution.Result == ResolutionResult.Resolved)
        {
            var tenantId = TenantIds.Format(tenant.TenantId);
            requestActivity?.SetTag(TenantTelemetry.TenantIdTag, tenantId);

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

        if (resolution.Result == ResolutionResult.AccessDenied)
        {
            TenantResolutionLog.TenantAccessDenied(
                _logger,
                context.Request.Method,
                context.Request.Path,
                context.User.Identity?.Name ?? "(anonymous)",
                TenantIds.Format(resolution.Tenant!.TenantId));
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

    private async ValueTask<Resolution> ResolveAsync(HttpContext context)
    {
        using var activity = TenantryHttpTelemetry.ActivitySource.StartActivity(TenantryHttpTelemetry.ResolveActivityName);

        var resolution = await FindTenantAsync(context);

        if (activity is not null)
        {
            activity.SetTag(TenantryHttpTelemetry.ResultTag, TenantryHttpTelemetry.ResultName(resolution.Result));

            if (resolution is { Result: ResolutionResult.Resolved, Tenant: { } tenant })
            {
                activity.SetTag(TenantTelemetry.TenantIdTag, TenantIds.Format(tenant.TenantId));
            }
        }

        return resolution;
    }

    // Another service sent a tenant id, which the store may not accept as an identifier (it may map slugs only).
    private ValueTask<ITenantDescriptor<TKey>?> LookUpAsync(string identifier, bool isTenantId, CancellationToken cancellationToken)
    {
        if (!isTenantId)
        {
            return _tenants.FindByIdentifierAsync(identifier, cancellationToken);
        }

        return TenantIds.TryParse<TKey>(identifier, out var tenantId)
            ? _tenants.GetTenantAsync(tenantId, cancellationToken)
            : ValueTask.FromResult<ITenantDescriptor<TKey>?>(null);
    }

    private async ValueTask<Resolution> FindTenantAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        string? identifier = null;
        var isTenantId = false;
        List<ClaimTenantResolver>? claimResolvers = null;

        foreach (var resolver in context.RequestServices.GetServices<ITenantResolver>())
        {
            identifier = await resolver.ResolveAsync(context, cancellationToken);

            // An empty identifier is no identifier: the next resolver may have one.
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                isTenantId = resolver is PropagationHeaderTenantResolver;
                break;
            }

            identifier = null;

            // The authentication middleware has not run yet: a claim resolver could not see the request's user.
            if (resolver is ClaimTenantResolver claimResolver && context.Features.Get<IAuthenticationFeature>() is null)
            {
                (claimResolvers ??= []).Add(claimResolver);
            }
        }

        if (identifier is null)
        {
            return new Resolution(ResolutionResult.Missing, null, null, claimResolvers);
        }

        var tenant = await LookUpAsync(identifier, isTenantId, cancellationToken);

        if (tenant is null)
        {
            return new Resolution(ResolutionResult.NotFound, identifier, null, null);
        }

        // An inactive (suspended) tenant is refused like one an access validator refuses.
        if (_activity is not null && !await _activity.IsActiveAsync(tenant, cancellationToken))
        {
            return new Resolution(ResolutionResult.AccessDenied, identifier, tenant, null);
        }

        if (_hasValidators)
        {
            foreach (var validator in context.RequestServices.GetServices<ITenantAccessValidator<TKey>>())
            {
                if (!await validator.ValidateAsync(context, tenant, cancellationToken))
                {
                    return new Resolution(ResolutionResult.AccessDenied, identifier, tenant, null);
                }
            }
        }

        return new Resolution(ResolutionResult.Resolved, identifier, tenant, null);
    }

    // Runs the rest of the pipeline, then looks for what the request needed and only had later: an endpoint routing
    // chose after this middleware ran, or a user the authentication middleware signed in after it (it sets
    // IAuthenticationFeature on every request it runs for, so a user signed in later by other code, such as
    // authorization with a scheme that is not the default, is not mistaken for it). Each is logged once.
    // Before routing, a request without a tenant still fails closed: an endpoint routing chooses that requires one is
    // replaced by one that rejects the request.
    private async Task NextAsync(HttpContext context, bool endpointWasNull, Resolution resolution)
    {
        RejectingEndpointFeature? routed = null;
        IEndpointFeature? previous = null;

        if (endpointWasNull && resolution.Result != ResolutionResult.Resolved)
        {
            previous = context.Features.Get<IEndpointFeature>();
            routed = new RejectingEndpointFeature(previous, endpoint => IsTenantRequired(endpoint)
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

                _metrics.Record(resolution.Result, routed.Rejected);
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
    private Endpoint RejectingEndpoint(Endpoint endpoint, Resolution resolution)
    {
        RequestDelegate reject = context => RejectAsync(context, resolution);

        return endpoint is RouteEndpoint route
            ? new RouteEndpoint(reject, route.RoutePattern, route.Order, route.Metadata, route.DisplayName)
            : new Endpoint(reject, endpoint.Metadata, endpoint.DisplayName);
    }

    private static bool HasTenantMetadata(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<RequireTenantAttribute>() is not null ||
        endpoint.Metadata.GetMetadata<AllowMissingTenantAttribute>() is not null;

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

    private async Task RejectAsync(HttpContext context, Resolution resolution)
    {
        var request = context.Request;

        // With access validators, a tenant that does not exist gets the same response as one the caller may not
        // use, so a caller cannot find out which tenants exist.
        var (statusCode, title, detail) = resolution.Result switch
        {
            ResolutionResult.Missing => (_options.MissingTenantStatusCode, "Tenant required",
                "This endpoint requires a tenant, and the request does not identify one."),
            ResolutionResult.NotFound when !_hasValidators => (_options.TenantNotFoundStatusCode, "Tenant not found",
                "The request's tenant does not exist."),
            _ => (_options.AccessDeniedStatusCode, "Tenant access denied", "The request may not use its tenant."),
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
            _ => TenantRejectionReason.AccessDenied,
        };

        TenantRejectedContext<TKey> rejected = new(
            context,
            reason,
            statusCode,
            resolution.Identifier,
            resolution.Result == ResolutionResult.AccessDenied ? resolution.Tenant : null);

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

    /// <summary>
    /// The endpoint feature while routing runs after this middleware: an endpoint routing sets that requires a
    /// tenant is replaced by the one the replacement function gives.
    /// </summary>
    private sealed class RejectingEndpointFeature(IEndpointFeature? inner, Func<Endpoint, Endpoint?> replace) : IEndpointFeature
    {
        private Endpoint? _endpoint;

        public bool Rejected { get; private set; }

        public Endpoint? Endpoint
        {
            get => inner is null ? _endpoint : inner.Endpoint;
            set
            {
                if (value is not null && replace(value) is { } replacement)
                {
                    value = replacement;
                    Rejected = true;
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
    }

    private sealed record Resolution(
        ResolutionResult Result,
        string? Identifier,
        ITenantDescriptor<TKey>? Tenant,
        List<ClaimTenantResolver>? MissedClaimResolvers);
}
