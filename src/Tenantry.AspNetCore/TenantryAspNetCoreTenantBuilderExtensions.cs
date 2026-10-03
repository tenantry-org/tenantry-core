using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    /// <param name="configure">Sets the base domains and the subdomains that are not tenants (<c>www</c> by default), or <see langword="null"/> for the defaults.</param>
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
    /// Resolves the tenant from the request's host name, such as <c>app.acme.com</c>, for tenants with domains of
    /// their own, as <see cref="HostTenantResolver"/> describes. Your tenant store's
    /// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/> maps the host name to a tenant.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the domains whose hosts are not tenants (<c>localhost</c> by default), or <see langword="null"/> for the defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// It resolves every host that is not an IP address or excluded, so resolvers added after it never run for those
    /// hosts: add it last. Exclude your own domain, so its hosts do not ask the store for a tenant on every request.
    /// </remarks>
    /// <example>
    /// <code>
    /// tenant
    ///     .ResolveFromSubdomain(o =&gt; o.BaseDomains.Add("example.com"))   // acme.example.com
    ///     .ResolveFromHost(o =&gt; o.ExcludedDomains.Add("example.com"));   // app.acme.com
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> ResolveFromHost<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<HostTenantResolverOptions>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        HostTenantResolverOptions options = new();
        configure?.Invoke(options);

        return builder.UseResolver(new HostTenantResolver(options));
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
    /// Resolves the tenant another service sent with its request: the tenant id in the
    /// <see cref="TenantPropagation.HeaderName"/> header, which Tenantry.Http's <c>UseTenantry()</c> adds to an
    /// HttpClient's or gRPC client's requests. The value is read as a tenant id (<see cref="TenantIds.TryParse{TKey}"/>)
    /// and looked up with the store's <see cref="ITenantStore{TKey}.GetTenantAsync"/>, not its
    /// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/>; a value that is not a tenant id finds no tenant.
    /// </summary>
    /// <remarks>
    /// A header is a claim, not proof: any caller that reaches the service can set it. Accept it only from callers you
    /// authenticate (a token, mutual TLS), and check that the caller may act for the tenant with
    /// <c>ValidateTenantAccess</c>. Resolvers run in the order they are added, and the first that finds a value wins.
    /// </remarks>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromPropagationHeader()
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .ValidateTenantAccess&lt;CallingServiceValidator&gt;());
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> ResolveFromPropagationHeader<TKey>(this ITenantBuilder<TKey> builder)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        builder.UseResolver(new PropagationHeaderTenantResolver());

    /// <summary>
    /// Keeps ASP.NET Core's output cache per tenant: a response cached while a tenant is current varies by the tenant,
    /// so it is served only to that tenant, and invalidating the tenant (<see cref="ITenantStoreCache{TKey}.Invalidate"/>)
    /// evicts it. A response for a request without a tenant (an endpoint that allows one to be missing) is cached apart
    /// from every tenant's.
    /// </summary>
    /// <remarks>
    /// Add the output cache after Tenantry in the pipeline (<c>app.UseTenantry()</c>, then <c>app.UseOutputCache()</c>),
    /// so the tenant is known when the cache runs. In the other order, a request the output cache handles throws,
    /// naming the fix, rather than being cached for every tenant. Endpoints still opt in to output caching themselves
    /// (<c>CacheOutput()</c>, <c>[OutputCache]</c>).
    /// </remarks>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddOutputCache();
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromSubdomain()
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .IsolateOutputCache());
    ///
    /// app.UseTenantry();
    /// app.UseOutputCache();
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> IsolateOutputCache<TKey>(this ITenantBuilder<TKey> builder)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OutputCacheOptions>, TenantOutputCacheSetup<TKey>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<TKey>, TenantOutputCacheInvalidation<TKey>>());

        return builder;
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
    /// Registers a custom <see cref="ITenantResolver"/> implementation, created through dependency injection in each
    /// request's scope, so it can depend on scoped services such as a <c>DbContext</c>.
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
        builder.Services.AddScoped<ITenantResolver, TResolver>();
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
    /// Configures how requests are treated: whether they need a tenant, the status code of each rejection, and the
    /// events raised when a request's tenant is made current or a request is rejected.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ConfigureResolution<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<TenantResolutionOptions<TKey>> configure)
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
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with <see cref="TenantResolutionOptions{TKey}.AccessDeniedStatusCode"/>, and any other runs without one.</param>
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
    /// <param name="validator">Returns <see langword="true"/> when the request may use the resolved tenant; otherwise an endpoint that needs a tenant refuses the request with <see cref="TenantResolutionOptions{TKey}.AccessDeniedStatusCode"/>, and any other runs without one. It receives the request's cancellation token.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder<TKey> ValidateTenantAccess<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(validator);

        TenantResolutionMiddlewareConfigurator<TKey>.Register(builder.Services);
        builder.Services.AddSingleton<ITenantAccessValidator<TKey>>(new DelegateTenantAccessValidator<TKey>(validator));
        return builder;
    }

    /// <summary>
    /// Adds an access validator of type <typeparamref name="TValidator"/>, created in each request's scope, so it can
    /// depend on scoped services such as a <c>DbContext</c>. Every validator must allow a request before its tenant is
    /// made current.
    /// </summary>
    /// <typeparam name="TValidator">The validator type, which implements <see cref="ITenantAccessValidator{TKey}"/> for the application's tenant key type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/>, without its key type: call methods that need it first.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TValidator"/> does not implement <see cref="ITenantAccessValidator{TKey}"/> for the builder's key type.</exception>
    public static ITenantBuilder ValidateTenantAccess<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        this ITenantBuilder builder)
        where TValidator : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(new TenantAccessValidatorRegistration<TValidator>());
        return builder;
    }
}
