using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Tenantry.AspNetCore.Extensions;
using Tenantry.AspNetCore.Resolution;
using Tenantry.Core;

namespace Tenantry.AspNetCore;

/// <summary>
/// Fluent builder for configuring multi-tenancy services.
/// Obtained from <see cref="ServiceCollectionExtensions.AddTenantry{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
public interface IAspNetCoreTenantBuilder<TKey> : ITenantBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Resolves the tenant from the specified HTTP request header.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromHeader(string headerName);

    /// <summary>
    /// Resolves the tenant from the first subdomain of the request host.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromSubdomain();

    /// <summary>
    /// Resolves the tenant from a route value.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromRouteValue(string routeValueKey = "tenant");

    /// <summary>
    /// Resolves the tenant from a claim on the current request principal.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromClaim(string claimType = "tenant_id");

    /// <summary>
    /// Resolves the tenant from a query string parameter.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromQueryString(string parameterName = "tenantId");

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> UseResolver<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TResolver : class, ITenantResolver;

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a concrete instance.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> UseResolver(ITenantResolver resolver);

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a factory method.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> UseResolver(Func<IServiceProvider, ITenantResolver> factory);

    /// <summary>
    /// Requires tenant resolution by default for requests that pass through Tenantry middleware.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> RequireTenantByDefault();

    /// <summary>
    /// Validates tenant access by matching the resolved tenant against claims on the current request principal.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessByClaim(string claimType);

    /// <summary>
    /// Adds a composite tenant access validator that succeeds when any configured validation group passes.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessAny(
        params Action<ITenantAccessValidationGroupBuilder<TKey>>[] groups);

    /// <summary>
    /// Adds a synchronous tenant access validator.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, bool> validator);

    /// <summary>
    /// Adds an asynchronous tenant access validator.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator);
}
