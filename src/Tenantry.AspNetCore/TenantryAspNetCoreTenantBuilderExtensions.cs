using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Tenantry;
using Tenantry.AspNetCore;
using Tenantry.AspNetCore.Internal;

// Builder extensions live in the builder's registration namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tenantry's ASP.NET Core features on <see cref="ITenantBuilder{TKey}"/>: how a request is resolved to a
/// tenant, whether endpoints need one, and who may use it. <c>app.UseTenantry()</c> applies them to requests.
/// </summary>
public static class TenantryAspNetCoreTenantBuilderExtensions
{
    /// <summary>
    /// Resolves the tenant from the specified HTTP request header.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="headerName">The name of the header that carries the tenant identifier, such as <c>X-Tenant-Id</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ResolveFromHeader<TKey>(this ITenantBuilder<TKey> builder, string headerName)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);

        return builder.UseResolver(new HeaderTenantResolver(headerName));
    }

    /// <summary>
    /// Resolves the tenant from the subdomain of the request host, as <see cref="SubdomainTenantResolver"/>
    /// describes.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the base domain and the subdomains that are not tenants (<c>www</c> by default), or <see langword="null"/> for the defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ResolveFromSubdomain<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<SubdomainTenantResolverOptions>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        SubdomainTenantResolverOptions options = new();
        configure?.Invoke(options);

        return builder.UseResolver(new SubdomainTenantResolver(options));
    }

    /// <summary>
    /// Resolves the tenant from a route value.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="routeValueKey">The name of the route value that carries the tenant identifier, as in <c>/api/{tenant}/orders</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ResolveFromRouteValue<TKey>(
        this ITenantBuilder<TKey> builder,
        string routeValueKey = "tenant")
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeValueKey);

        return builder.UseResolver(new RouteValueTenantResolver(routeValueKey));
    }

    /// <summary>
    /// Resolves the tenant from a claim on the current request principal.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="claimType">The type of the claim that carries the tenant identifier.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ResolveFromClaim<TKey>(this ITenantBuilder<TKey> builder, string claimType = "tenant_id")
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        return builder.UseResolver(new ClaimTenantResolver(claimType));
    }

    /// <summary>
    /// Resolves the tenant from a query string parameter.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="parameterName">The name of the query string parameter that carries the tenant identifier.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <strong>For local development and testing only — do not use in production.</strong> Query string
    /// parameters are routinely logged by servers, proxies, and analytics, and are trivially spoofable,
    /// so they are not a safe tenant-resolution mechanism for production traffic.
    /// </remarks>
    public static ITenantBuilder<TKey> ResolveFromQueryString<TKey>(
        this ITenantBuilder<TKey> builder,
        string parameterName = "tenantId")
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        return builder.UseResolver(new QueryStringTenantResolver(parameterName));
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> implementation, created through dependency injection as
    /// a singleton.
    /// </summary>
    /// <typeparam name="TResolver">The resolver type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/>, without its key type: call methods that need it first.</returns>
    public static ITenantBuilder UseResolver<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>(
        this ITenantBuilder builder)
        where TResolver : class, ITenantResolver
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(TenantResolutionRegistration.Instance);
        builder.Services.AddSingleton<ITenantResolver, TResolver>();
        return builder;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> instance.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="resolver">The resolver to use for every request.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> UseResolver<TKey>(this ITenantBuilder<TKey> builder, ITenantResolver resolver)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolver);

        TenantResolutionMiddlewareConfigurator<TKey>.Register(builder.Services);
        builder.Services.AddSingleton(resolver);
        return builder;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantResolver"/> created by a factory, as a singleton.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="factory">Creates the resolver from the application's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> UseResolver<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<IServiceProvider, ITenantResolver> factory)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        TenantResolutionMiddlewareConfigurator<TKey>.Register(builder.Services);
        builder.Services.AddSingleton(factory);
        return builder;
    }

    /// <summary>
    /// Requires a tenant on every endpoint that does not allow a missing one with <c>AllowMissingTenant()</c>.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> RequireTenantByDefault<TKey>(this ITenantBuilder<TKey> builder)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        builder.ConfigureResolution(options => options.RequireTenantByDefault = true);

    /// <summary>
    /// Configures how requests are treated: whether they need a tenant, and the status code of each rejection.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ConfigureResolution<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<TenantResolutionOptions> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        TenantResolutionMiddlewareConfigurator<TKey>.Register(builder.Services);
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Validates tenant access by matching the resolved tenant against claims on the current request principal.
    /// Supports repeated claims with single tenant ids and JSON array claim values.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="claimType">The type of the claims that list the tenant identifiers the principal may use. A request for any other tenant is refused.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ValidateTenantAccessByClaim<TKey>(this ITenantBuilder<TKey> builder, string claimType)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        return builder.ValidateTenantAccess((httpContext, tenant, _) =>
            ClaimTenantAccessValidator.ValidateAsync(httpContext, tenant, claimType));
    }

    /// <summary>
    /// Adds a synchronous tenant access validator. Every validator must allow a request before its tenant is
    /// made current.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with <see cref="TenantResolutionOptions.AccessDeniedStatusCode"/>, and any other runs without one.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<HttpContext, ITenantDescriptor<TKey>, bool> validator)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(validator);

        return builder.ValidateTenantAccess((httpContext, tenant, _) =>
            ValueTask.FromResult(validator(httpContext, tenant)));
    }

    /// <summary>
    /// Adds an asynchronous tenant access validator. Every validator must allow a request before its tenant is
    /// made current.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with <see cref="TenantResolutionOptions.AccessDeniedStatusCode"/>, and any other runs without one. It receives the request's cancellation token.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(validator);

        TenantResolutionMiddlewareConfigurator<TKey>.Register(builder.Services);
        builder.Services.Configure<TenantAccessOptions<TKey>>(options => options.Validators.Add(validator));
        return builder;
    }
}
