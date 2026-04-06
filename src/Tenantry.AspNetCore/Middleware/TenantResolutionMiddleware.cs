using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenantry.AspNetCore.Attributes;
using Tenantry.AspNetCore.Internal;
using Tenantry.AspNetCore.Resolution;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Middleware;

/// <summary>
/// Resolves the current tenant from the HTTP request and populates
/// <see cref="ITenantContext{TKey}"/> for the duration of the request scope.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
/// <remarks>
/// Registered via <c>app.UseTenantry{TKey}()</c>.
/// Resolvers are tried in registration order; the first non-null result wins.
/// If no resolver matches, the request continues without a tenant context.
/// The resolved tenant ID is added to the logging scope for structured log correlation.
/// </remarks>
internal sealed class TenantResolutionMiddleware<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly RequestDelegate _next;
    private readonly IEnumerable<ITenantResolver> _resolvers;
    private readonly ILogger<TenantResolutionMiddleware<TKey>> _logger;

    /// <summary>
    /// Creates the middleware used to resolve the current tenant from each HTTP request.
    /// </summary>
    /// <param name="next">The next middleware delegate in the request pipeline.</param>
    /// <param name="resolvers">The tenant resolvers evaluated in registration order.</param>
    /// <param name="logger">The logger used for tenant resolution diagnostics and warnings.</param>
    public TenantResolutionMiddleware(
        RequestDelegate next,
        IEnumerable<ITenantResolver> resolvers,
        ILogger<TenantResolutionMiddleware<TKey>> logger)
    {
        _next = next;
        _resolvers = resolvers;
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
        // Resolved from request services to avoid placing an internal type in the public constructor signature.
        var tenantScope = context.RequestServices.GetRequiredService<ITenantScope<TKey>>();
        var resolutionOptions = context.RequestServices.GetRequiredService<TenantResolutionOptions<TKey>>();

        string? rawTenantId = null;

        foreach (var resolver in _resolvers)
        {
            rawTenantId = await resolver.ResolveAsync(context, context.RequestAborted);
            if (rawTenantId is not null)
            {
                break;
            }
        }

        if (rawTenantId is null)
        {
            if (IsTenantRequired(context, resolutionOptions))
            {
                _logger.LogWarning(
                    "No tenant resolved from request {Method} {Path}. Tenant resolution is required. Returning {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    resolutionOptions.MissingTenantStatusCode);

                context.Response.StatusCode = resolutionOptions.MissingTenantStatusCode;
                await context.Response.WriteAsync("Tenant resolution is required for this endpoint.", context.RequestAborted);
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

        if (!TKey.TryParse(rawTenantId, null, out var tenantId))
        {
            _logger.LogWarning("Tenant ID '{RawTenantId}' could not be parsed as {TKeyType}. Returning 400",
                rawTenantId,
                typeof(TKey).Name);
            
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Invalid tenant ID format: '{rawTenantId}'.", context.RequestAborted);
            return;
        }

        var tenant = await tenantStore.GetTenantAsync(tenantId, context.RequestAborted);

        if (tenant is null)
        {
            _logger.LogWarning("Tenant '{TenantId}' not found in store. Returning 404", rawTenantId);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"Tenant '{rawTenantId}' not found.", context.RequestAborted);
            return;
        }

        if (resolutionOptions.AccessValidators.Count > 0)
        {
            var allowed = await IsTenantAccessAllowed(context, tenant, resolutionOptions, context.RequestAborted);
            if (!allowed)
            {
                _logger.LogWarning(
                    "Tenant access denied for request {Method} {Path}. User '{User}' is not authorised for tenant '{TenantId}'",
                    context.Request.Method,
                    context.Request.Path,
                    context.User.Identity?.Name ?? "(anonymous)",
                    tenant.TenantId);

                context.Response.StatusCode = resolutionOptions.AccessDeniedStatusCode;
                await context.Response.WriteAsync("Tenant access denied.", context.RequestAborted);
                return;
            }
        }
        
        using var _ = tenantScope.BeginScope(tenant);
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

    private static bool IsTenantRequired(HttpContext context, TenantResolutionOptions<TKey> options)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            return options.RequireTenantByDefault;
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

        return options.RequireTenantByDefault;
    }

    private static async ValueTask<bool> IsTenantAccessAllowed(
        HttpContext context,
        ITenantDescriptor<TKey> tenant,
        TenantResolutionOptions<TKey> options,
        CancellationToken cancellationToken)
    {
        foreach (var validator in options.AccessValidators)
        {
            if (!await validator(context, tenant, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}
