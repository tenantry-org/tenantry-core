using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// order, and the first identifier one returns is looked up with <see cref="ITenantStoreAccessor{TKey}"/> (through
/// the cache, with <c>CacheTenants</c>). The access validators, also from the request's scope, must all allow the
/// tenant. The tenant is then current for the rest of the request, which is tagged <c>tenant.id</c> and logged with a
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
    private readonly ITenantStoreAccessor<TKey> _tenants;
    private readonly ITenantContextSetter<TKey> _tenantContext;
    private readonly TenantResolutionOptions<TKey> _options;
    private readonly TenantResolutionMetrics _metrics;
    private readonly ILogger _logger;
    private readonly bool _hasValidators;
    private int _warnedBeforeRouting;
    private int _warnedBeforeAuthentication;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ITenantStoreAccessor<TKey> tenants,
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

        // Without IServiceProviderIsService, validators are assumed to exist: an unknown tenant then gets the
        // access-denied response, which hides which tenants exist either way.
        _hasValidators = services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantAccessValidator<TKey>)) ?? true;
    }

    /// <summary>
    /// Attempts to resolve and set the current tenant, then continues the pipeline.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Captured first: the resolution's own activity is the current one while it runs.
        var requestActivity = Activity.Current;
        var endpointWasNull = context.GetEndpoint() is null;
        var required = IsTenantRequired(context);
        var resolution = await ResolveAsync(context);

        _metrics.Record(resolution.Result, rejected: required && resolution.Result != ResolutionResult.Resolved);

        if (resolution.Tenant is { } tenant && resolution.Result == ResolutionResult.Resolved)
        {
            var tenantId = TenantryHttpTelemetry.Format(tenant.TenantId);
            requestActivity?.SetTag(TenantryHttpTelemetry.TenantIdTag, tenantId);

            using var current = _tenantContext.Use(tenant);
            using var logScope = _logger.BeginScope(new TenantLogScope(tenantId));

            TenantResolutionLog.TenantResolved(_logger, tenantId, context.Request.Method, context.Request.Path);

            if (_options.OnResolved is { } onResolved)
            {
                await onResolved(new TenantResolvedContext<TKey>(context, tenant));
            }

            await NextAsync(context, endpointWasNull, resolution);
            return;
        }

        if (resolution.Result == ResolutionResult.AccessDenied)
        {
            TenantResolutionLog.TenantAccessDenied(
                _logger,
                context.Request.Method,
                context.Request.Path,
                context.User.Identity?.Name ?? "(anonymous)",
                TenantryHttpTelemetry.Format(resolution.Tenant!.TenantId));
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

        await NextAsync(context, endpointWasNull, resolution);
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
                activity.SetTag(TenantryHttpTelemetry.TenantIdTag, TenantryHttpTelemetry.Format(tenant.TenantId));
            }
        }

        return resolution;
    }

    private async ValueTask<Resolution> FindTenantAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        string? identifier = null;
        List<ClaimTenantResolver>? claimResolvers = null;

        foreach (var resolver in context.RequestServices.GetServices<ITenantResolver>())
        {
            identifier = await resolver.ResolveAsync(context, cancellationToken);

            // An empty identifier is no identifier: the next resolver may have one.
            if (!string.IsNullOrWhiteSpace(identifier))
            {
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

        var tenant = await _tenants.FindByIdentifierAsync(identifier, cancellationToken);

        if (tenant is null)
        {
            return new Resolution(ResolutionResult.NotFound, identifier, null, null);
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
    private async Task NextAsync(HttpContext context, bool endpointWasNull, Resolution resolution)
    {
        await _next(context);

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

    private static bool HasTenantMetadata(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<RequireTenantAttribute>() is not null ||
        endpoint.Metadata.GetMetadata<AllowMissingTenantAttribute>() is not null;

    private bool IsTenantRequired(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

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

    private sealed record Resolution(
        ResolutionResult Result,
        string? Identifier,
        ITenantDescriptor<TKey>? Tenant,
        List<ClaimTenantResolver>? MissedClaimResolvers);
}
