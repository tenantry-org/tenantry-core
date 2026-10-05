using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// What resolution found for a request, and the resolvers tried before the one that found it (or all, when none did)
/// that found nothing because what they read was not there yet: a claim resolver before authentication, and a
/// route-value resolver before routing.
/// </summary>
internal sealed record TenantResolution<TKey>(
    ResolutionResult Result,
    string? Identifier,
    ITenantDescriptor<TKey>? Tenant,
    List<ITenantResolver>? MissedResolvers)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Whether a missed resolver of this kind finds an identifier now that the pipeline has given it what it reads.
    /// </summary>
    public async ValueTask<bool> ResolvesNowAsync<TResolver>(HttpContext context)
        where TResolver : ITenantResolver
    {
        foreach (var resolver in MissedResolvers?.OfType<TResolver>() ?? [])
        {
            if (!string.IsNullOrWhiteSpace(await resolver.ResolveAsync(context, context.RequestAborted).ConfigureAwait(false)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Finds a request's tenant: the resolvers in registration order, the store lookup, the access validators, and the
/// activity check. <c>app.UseTenantry()</c> runs all of it; <c>app.UseTenantResolution()</c>, before authentication, runs
/// only the resolvers added before the first that needs the user (a claim or propagation header resolver), and no
/// access validators. When those find nothing,
/// <c>app.UseTenantry()</c> runs every resolver again, in order, after authentication.
/// </summary>
internal sealed class TenantRequestResolution<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ITenantLookup<TKey> _tenants;
    private readonly ITenantActivity<TKey>? _activity;

    public TenantRequestResolution(ITenantLookup<TKey> tenants, IServiceProvider services)
    {
        _tenants = tenants;
        _activity = services.GetService<ITenantActivity<TKey>>();

        // Without IServiceProviderIsService, validators are assumed to exist: an unknown tenant then gets the
        // access-denied response, which hides which tenants exist either way.
        HasValidators = services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantAccessValidator<TKey>)) ?? true;
    }

    public bool HasValidators { get; }

    /// <param name="context">The request.</param>
    /// <param name="beforeAuthentication">
    /// Whether the authentication middleware is still to run: claim resolvers and access validators, which read the
    /// user, are left for <c>app.UseTenantry()</c>.
    /// </param>
    public async ValueTask<TenantResolution<TKey>> ResolveAsync(HttpContext context, bool beforeAuthentication)
    {
        // The span is named for what it covers, not for this method.
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = TenantryHttpTelemetry.ActivitySource.StartActivity(TenantryHttpTelemetry.ResolveActivityName);

        var resolution = await FindTenantAsync(context, beforeAuthentication).ConfigureAwait(false);

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

    /// <summary>Whether every access validator allows the request to use <paramref name="tenant"/>.</summary>
    public async ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<TKey> tenant)
    {
        if (!HasValidators)
        {
            return true;
        }

        foreach (var validator in context.RequestServices.GetServices<ITenantAccessValidator<TKey>>())
        {
            if (!await validator.ValidateAsync(context, tenant, context.RequestAborted).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
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

    private async ValueTask<TenantResolution<TKey>> FindTenantAsync(HttpContext context, bool beforeAuthentication)
    {
        var cancellationToken = context.RequestAborted;
        string? identifier = null;
        var isTenantId = false;
        List<ITenantResolver>? missedResolvers = null;

        foreach (var resolver in context.RequestServices.GetServices<ITenantResolver>())
        {
            // Before authentication there is no user to read, for a claim or to trust a propagated tenant. Stop here rather
            // than skip it, so a resolver added after it never wins over it: app.UseTenantry() resolves again, in order,
            // once the user is known.
            if (beforeAuthentication && resolver is ClaimTenantResolver or PropagationHeaderTenantResolver)
            {
                break;
            }

            identifier = await resolver.ResolveAsync(context, cancellationToken).ConfigureAwait(false);

            // An empty identifier is no identifier: the next resolver may have one.
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                isTenantId = resolver is PropagationHeaderTenantResolver;
                break;
            }

            identifier = null;

            // The authentication middleware has not run yet, so a claim resolver could not see the request's user, or
            // routing has not, so a route-value resolver could not see its route values.
            if ((resolver is ClaimTenantResolver && context.Features.Get<IAuthenticationFeature>() is null) ||
                (resolver is RouteValueTenantResolver && context.GetEndpoint() is null))
            {
                (missedResolvers ??= []).Add(resolver);
            }
        }

        if (identifier is null)
        {
            return new(ResolutionResult.Missing, null, null, missedResolvers);
        }

        var tenant = await LookUpAsync(identifier, isTenantId, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return new(ResolutionResult.NotFound, identifier, null, missedResolvers);
        }

        // The access validators first, so only a caller they allow can learn that a tenant is suspended. Before
        // authentication they cannot run yet: app.UseTenantry() runs them on an inactive tenant too (CompleteAsync).
        if (!beforeAuthentication && !await ValidateAsync(context, tenant).ConfigureAwait(false))
        {
            return new(ResolutionResult.AccessDenied, identifier, tenant, missedResolvers);
        }

        if (_activity is not null && !await _activity.IsActiveAsync(tenant, cancellationToken).ConfigureAwait(false))
        {
            return new(ResolutionResult.Inactive, identifier, tenant, missedResolvers);
        }

        return new(ResolutionResult.Resolved, identifier, tenant, missedResolvers);
    }
}

/// <summary>
/// What <c>app.UseTenantResolution()</c> found for a request, for <c>app.UseTenantry()</c> to complete after
/// authentication.
/// </summary>
internal sealed class EarlyTenantResolution<TKey>(TenantResolution<TKey> resolution)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public TenantResolution<TKey> Resolution { get; } = resolution;

    /// <summary>Whether <c>app.UseTenantry()</c> has run its access validators on it.</summary>
    public bool Completed { get; set; }

    /// <summary>Whether the authorization middleware ran for the request before <c>app.UseTenantResolution()</c>.</summary>
    public bool AuthorizedBefore { get; init; }

    /// <summary>
    /// Whether <c>app.UseTenantry()</c> refused the request because the validators refused the tenant for a user
    /// signed in while it was current: the response then carries none of the cookies set after
    /// <c>app.UseTenantResolution()</c>.
    /// </summary>
    public bool RefusedSignedIn { get; set; }
}
