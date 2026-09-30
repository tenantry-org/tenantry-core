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
/// Registered via <c>app.UseTenantry()</c>. Resolvers are tried in registration order; the first non-null result
/// wins. The resolved tenant ID is added to the logging scope for structured log correlation.
/// </para>
/// <para>
/// An endpoint that needs a tenant rejects a request without a usable one, with the status code from
/// <see cref="TenantResolutionOptions"/> and, when an <see cref="IProblemDetailsService"/> is registered, a problem
/// details body. Any other endpoint runs without a tenant: a request whose identifier is not valid, names no tenant,
/// or names one an access validator refuses is treated as one without an identifier, so it tells a caller nothing
/// about which tenants exist.
/// </para>
/// </remarks>
internal sealed class TenantResolutionMiddleware<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly RequestDelegate _next;
    private readonly IEnumerable<ITenantResolver> _resolvers;
    private readonly ITenantContextSetter<TKey> _tenantContext;
    private readonly TenantResolutionOptions _options;
    private readonly TenantAccessOptions<TKey> _access;
    private readonly ILogger<TenantResolutionMiddleware<TKey>> _logger;

    /// <summary>
    /// Creates the middleware used to resolve the current tenant from each HTTP request.
    /// </summary>
    /// <param name="next">The next middleware delegate in the request pipeline.</param>
    /// <param name="resolvers">The tenant resolvers evaluated in registration order.</param>
    /// <param name="tenantContext">Makes the resolved tenant current.</param>
    /// <param name="options">Whether requests need a tenant, and the status codes of rejections.</param>
    /// <param name="access">The access validators.</param>
    /// <param name="logger">The logger used for tenant resolution diagnostics and warnings.</param>
    public TenantResolutionMiddleware(
        RequestDelegate next,
        IEnumerable<ITenantResolver> resolvers,
        ITenantContextSetter<TKey> tenantContext,
        IOptions<TenantResolutionOptions> options,
        IOptions<TenantAccessOptions<TKey>> access,
        ILogger<TenantResolutionMiddleware<TKey>> logger)
    {
        _next = next;
        _resolvers = resolvers;
        _tenantContext = tenantContext;
        _options = options.Value;
        _access = access.Value;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to resolve and set the current tenant, then continues the pipeline.
    /// </summary>
    /// <remarks>
    /// <paramref name="tenantStore"/> is resolved from the request scope, allowing
    /// implementations backed by a scoped DbContext to work correctly.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context, ITenantStore<TKey> tenantStore)
    {
        string? rawTenantId = null;

        foreach (var resolver in _resolvers)
        {
            rawTenantId = await resolver.ResolveAsync(context, context.RequestAborted);
            if (rawTenantId is not null)
            {
                break;
            }
        }

        var required = IsTenantRequired(context);

        if (rawTenantId is null)
        {
            if (required)
            {
                _logger.LogWarning(
                    "No tenant resolved from request {Method} {Path}. Tenant resolution is required. Returning {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    _options.MissingTenantStatusCode);

                await RejectAsync(
                    context,
                    _options.MissingTenantStatusCode,
                    "Tenant required",
                    "This endpoint requires a tenant, and the request does not identify one.");
                return;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("No tenant resolved from request {Method} {Path}. Continuing without tenant context",
                    context.Request.Method,
                    context.Request.Path);
            }

            await _next(context);
            return;
        }

        // A default id (Guid.Empty, 0) or an empty string means "no tenant" to Tenantry, so no tenant can have it.
        if (!TKey.TryParse(rawTenantId, null, out var tenantId)
            || tenantId is string { Length: 0 }
            || EqualityComparer<TKey>.Default.Equals(tenantId, default!))
        {
            if (required)
            {
                _logger.LogWarning("Tenant ID '{RawTenantId}' is not a valid {TKeyType}. Returning {StatusCode}",
                    rawTenantId,
                    typeof(TKey).Name,
                    _options.InvalidTenantStatusCode);

                await RejectAsync(
                    context,
                    _options.InvalidTenantStatusCode,
                    "Invalid tenant",
                    "The request's tenant identifier is not valid.");
                return;
            }

            LogContinuingWithoutTenant(context, "is not a valid tenant id");
            await _next(context);
            return;
        }

        var tenant = await tenantStore.GetTenantAsync(tenantId, context.RequestAborted);

        if (tenant is null)
        {
            if (!required)
            {
                LogContinuingWithoutTenant(context, "names no tenant");
                await _next(context);
                return;
            }

            _logger.LogWarning("Tenant '{TenantId}' not found in store", tenantId);

            // With access validators, a tenant that does not exist gets the same response as one the caller may
            // not use, so a caller cannot find out which tenants exist.
            if (_access.Validators.Count > 0)
            {
                await RejectAccessDeniedAsync(context);
                return;
            }

            await RejectAsync(
                context,
                _options.TenantNotFoundStatusCode,
                "Tenant not found",
                "The request's tenant does not exist.");
            return;
        }

        if (_access.Validators.Count > 0 && !await IsTenantAccessAllowed(context, tenant, context.RequestAborted))
        {
            _logger.LogWarning(
                "Tenant access denied for request {Method} {Path}. User '{User}' is not authorised for tenant '{TenantId}'",
                context.Request.Method,
                context.Request.Path,
                context.User.Identity?.Name ?? "(anonymous)",
                tenant.TenantId);

            if (required)
            {
                await RejectAccessDeniedAsync(context);
                return;
            }

            LogContinuingWithoutTenant(context, "names a tenant the request may not use");
            await _next(context);
            return;
        }

        using var _ = _tenantContext.Use(tenant);
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenant.TenantId.ToString()!,
            ["TenantName"] = tenant.Name,
        });

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Tenant '{TenantId}' resolved for {Method} {Path}",
                tenant.TenantId,
                context.Request.Method,
                context.Request.Path);
        }

        await _next(context);
    }

    private void LogContinuingWithoutTenant(HttpContext context, string reason)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "The tenant identifier of request {Method} {Path} {Reason}. The endpoint does not require a tenant, " +
                "so it continues without one",
                context.Request.Method,
                context.Request.Path,
                reason);
        }
    }

    private bool IsTenantRequired(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            return _options.RequireTenantByDefault;
        }

        for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
        {
            var metadata = endpoint.Metadata[i];

            switch (metadata)
            {
                case AllowMissingTenantAttribute:
                    return false;
                case RequireTenantAttribute:
                    return true;
            }
        }

        return _options.RequireTenantByDefault;
    }

    private async ValueTask<bool> IsTenantAccessAllowed(
        HttpContext context,
        ITenantDescriptor<TKey> tenant,
        CancellationToken cancellationToken)
    {
        foreach (var validator in _access.Validators)
        {
            if (!await validator(context, tenant, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private Task RejectAccessDeniedAsync(HttpContext context) =>
        RejectAsync(
            context,
            _options.AccessDeniedStatusCode,
            "Tenant access denied",
            "The request may not use its tenant.");

    // Never repeats the identifier the request sent.
    private static async Task RejectAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;

        if (context.RequestServices.GetService<IProblemDetailsService>() is { } problemDetails)
        {
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = statusCode, Title = title, Detail = detail },
            });
        }
    }
}
