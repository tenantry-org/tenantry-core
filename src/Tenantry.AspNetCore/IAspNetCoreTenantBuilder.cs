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
    /// <param name="headerName">The name of the header that carries the tenant identifier, such as <c>X-Tenant-Id</c>.</param>
    IAspNetCoreTenantBuilder<TKey> ResolveFromHeader(string headerName);

    /// <summary>
    /// Resolves the tenant from the first subdomain of the request host.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> ResolveFromSubdomain();

    /// <summary>
    /// Resolves the tenant from a route value.
    /// </summary>
    /// <param name="routeValueKey">The name of the route value that carries the tenant identifier, as in <c>/api/{tenant}/orders</c>.</param>
    IAspNetCoreTenantBuilder<TKey> ResolveFromRouteValue(string routeValueKey = "tenant");

    /// <summary>
    /// Resolves the tenant from a claim on the current request principal.
    /// </summary>
    /// <param name="claimType">The type of the claim that carries the tenant identifier.</param>
    IAspNetCoreTenantBuilder<TKey> ResolveFromClaim(string claimType = "tenant_id");

    /// <summary>
    /// Resolves the tenant from a query string parameter.
    /// </summary>
    /// <param name="parameterName">The name of the query string parameter that carries the tenant identifier.</param>
    /// <remarks>
    /// <strong>For local development and testing only — do not use in production.</strong> Query string
    /// parameters are routinely logged by servers, proxies, and analytics, and are trivially spoofable,
    /// so they are not a safe tenant-resolution mechanism for production traffic.
    /// </remarks>
    IAspNetCoreTenantBuilder<TKey> ResolveFromQueryString(string parameterName = "tenantId");

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation.
    /// </summary>
    /// <typeparam name="TResolver">The resolver type, created through dependency injection.</typeparam>
    IAspNetCoreTenantBuilder<TKey> UseResolver<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TResolver : class, ITenantResolver;

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a concrete instance.
    /// </summary>
    /// <param name="resolver">The resolver to use for every request.</param>
    IAspNetCoreTenantBuilder<TKey> UseResolver(ITenantResolver resolver);

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation with a factory method.
    /// </summary>
    /// <param name="factory">Creates the resolver from the application's services.</param>
    IAspNetCoreTenantBuilder<TKey> UseResolver(Func<IServiceProvider, ITenantResolver> factory);

    /// <summary>
    /// Requires tenant resolution by default for requests that pass through Tenantry middleware.
    /// </summary>
    IAspNetCoreTenantBuilder<TKey> RequireTenantByDefault();

    /// <summary>
    /// Validates tenant access by matching the resolved tenant against claims on the current request principal.
    /// </summary>
    /// <param name="claimType">The type of the claims that list the tenant identifiers the principal may use. A request for any other tenant is refused.</param>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccessByClaim(string claimType);

    /// <summary>
    /// Adds a synchronous tenant access validator.
    /// </summary>
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise the request is refused with <c>403 Forbidden</c>.</param>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(Func<HttpContext, ITenantDescriptor<TKey>, bool> validator);

    /// <summary>
    /// Adds an asynchronous tenant access validator.
    /// </summary>
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise the request is refused with <c>403 Forbidden</c>. It receives the request's cancellation token.</param>
    IAspNetCoreTenantBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator);
}
