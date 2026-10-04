using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Non-generic interface that enables <c>UseTenantry()</c> without repeating the TKey type parameter.
/// Registered, closed over the tenant key type, by the first ASP.NET Core feature added to the builder.
/// </summary>
internal interface ITenantResolutionMiddlewareConfigurator
{
    IApplicationBuilder Use(IApplicationBuilder app);

    IApplicationBuilder UseResolution(IApplicationBuilder app);
}

internal sealed class TenantResolutionMiddlewareConfigurator<TKey> : ITenantResolutionMiddlewareConfigurator
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Registers what <c>app.UseTenantry()</c> needs, once. Every ASP.NET Core builder method calls it.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddSingleton<ITenantResolutionMiddlewareConfigurator>(new TenantResolutionMiddlewareConfigurator<TKey>());
        services.TryAddSingleton(sp => new TenantResolutionMetrics(sp.GetService<IMeterFactory>()));
        services.TryAddSingleton<TenantryPipeline>();
        services.TryAddSingleton(sp => new TenantRequestResolution<TKey>(sp.GetRequiredService<ITenantLookup<TKey>>(), sp));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, TenantryPipelineCheck>());
    }

    // Whether app.UseAuthorization() was in the pipeline when app.UseTenantResolution() was added.
    private const string AuthorizationBeforeEarlyResolutionKey = "Tenantry.AuthorizationBeforeEarlyResolution";

    // Checks the registration when the pipeline is built, so a web application fails as it starts.
    public IApplicationBuilder Use(IApplicationBuilder app)
    {
        CheckRegistration(app);

        // Authorization between the two steps would run on a tenant the access validators have not checked.
        if (app.Properties.TryGetValue(AuthorizationBeforeEarlyResolutionKey, out var before) && before is false &&
            app.Properties.ContainsKey(AuthorizationMarkers.MiddlewareAdded))
        {
            throw new InvalidOperationException(
                "app.UseAuthorization() is between app.UseTenantResolution() and app.UseTenantry(), so authorization " +
                "would run on a tenant the access validators have not checked. Call app.UseAuthorization() after " +
                "app.UseTenantry().");
        }

        app.ApplicationServices.GetRequiredService<TenantryPipeline>().HasMiddleware = true;

        return app.UseMiddleware<TenantResolutionMiddleware<TKey>>();
    }

    public IApplicationBuilder UseResolution(IApplicationBuilder app)
    {
        CheckRegistration(app);
        var pipeline = app.ApplicationServices.GetRequiredService<TenantryPipeline>();

        // The order checks read keys ASP.NET Core does not document: say so if this version does not set them.
        if (!pipeline.HasEarlyResolution && AuthorizationMarkers.Missing(app.ApplicationServices) is [_, ..] missing)
        {
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(TenantResolutionLog.Category);
            TenantResolutionLog.AuthorizationMarkersMissing(logger, string.Join(", ", missing));
        }

        pipeline.HasEarlyResolution = true;
        var authorizationBefore = app.Properties.ContainsKey(AuthorizationMarkers.MiddlewareAdded);
        app.Properties[AuthorizationBeforeEarlyResolutionKey] = authorizationBefore;

        return app.UseMiddleware<TenantEarlyResolutionMiddleware<TKey>>();
    }

    private static void CheckRegistration(IApplicationBuilder app)
    {
        var services = app.ApplicationServices;
        var serviceTypes = services.GetService<IServiceProviderIsService>();

        // Checked without resolving the resolvers, which may be scoped.
        if (serviceTypes?.IsService(typeof(ITenantResolver)) == false)
        {
            throw new InvalidOperationException(
                $"app.UseTenantry() has no tenant resolvers for tenant key type '{typeof(TKey).Name}'. Add at least one " +
                "in AddTenantry, such as tenant.ResolveFromHeader(...), tenant.ResolveFromRouteValue(...), " +
                "tenant.ResolveFromClaim(...), tenant.ResolveFromSubdomain(), tenant.ResolveFromHost() or " +
                "tenant.UseResolver(...).");
        }

        // Checked without resolving the store, which may be scoped and create a DbContext.
        if (serviceTypes?.IsService(typeof(ITenantStore<TKey>)) == false)
        {
            throw new InvalidOperationException(
                $"app.UseTenantry() has no tenant store for ITenantStore<{typeof(TKey).Name}>. Register one in " +
                "AddTenantry, with tenant.UseStore<TStore>() or tenant.UseInMemoryStore(...).");
        }
    }
}

/// <summary>
/// Records that <c>app.UseTenantry()</c> added the middleware to the application's pipeline.
/// </summary>
internal sealed class TenantryPipeline
{
    private volatile bool _hasMiddleware;
    private volatile bool _hasEarlyResolution;

    public bool HasMiddleware
    {
        get => _hasMiddleware;
        set => _hasMiddleware = value;
    }

    /// <summary>Whether <c>app.UseTenantResolution()</c> is in the pipeline.</summary>
    public bool HasEarlyResolution
    {
        get => _hasEarlyResolution;
        set => _hasEarlyResolution = value;
    }
}

/// <summary>
/// Fails the web application's start when Tenantry resolves requests but <c>app.UseTenantry()</c> is not in the
/// pipeline: every request would run without a tenant, including on endpoints that require one.
/// </summary>
/// <remarks>
/// The host runs startup filters when it builds the request pipeline, after the application configured it, so the
/// check sees every <c>UseTenantry()</c> call, in a branch too. A host that serves no requests builds no pipeline,
/// and is not checked.
/// </remarks>
internal sealed class TenantryPipelineCheck(TenantryPipeline pipeline) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);

            if (pipeline is { HasMiddleware: false, HasEarlyResolution: true })
            {
                throw new InvalidOperationException(
                    "app.UseTenantResolution() is in the request pipeline but app.UseTenantry() is not, so no request's " +
                    "tenant would be checked by the access validators. Call app.UseTenantry() after app.UseAuthentication().");
            }

            if (!pipeline.HasMiddleware)
            {
                throw new InvalidOperationException(
                    "Tenantry is registered to resolve requests to tenants (AddTenantry with a ResolveFrom... or " +
                    "UseResolver method), but app.UseTenantry() is not in the request pipeline, so no request would " +
                    "have a tenant, and endpoints that require one would run without it. Call app.UseTenantry() " +
                    "after app.UseAuthentication() and before the endpoints.");
            }
        };
}

/// <summary>
/// Registers the ASP.NET Core services for the builder's key type, for builder methods on the non-generic
/// <see cref="ITenantBuilder"/>.
/// </summary>
internal sealed class TenantResolutionRegistration : ITenantRegistration
{
    public static TenantResolutionRegistration Instance { get; } = new();

    public void Apply<TKey>(ITenantBuilder<TKey> tenant)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        TenantResolutionMiddlewareConfigurator<TKey>.Register(tenant.Services);
}

/// <summary>
/// Registers an access validator type for the builder's key type, created in each request's scope.
/// </summary>
internal sealed class TenantAccessValidatorRegistration<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator> : ITenantRegistration
    where TValidator : class
{
    public void Apply<TKey>(ITenantBuilder<TKey> tenant)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (!typeof(ITenantAccessValidator<TKey>).IsAssignableFrom(typeof(TValidator)))
        {
            throw new InvalidOperationException(
                $"{typeof(TValidator).Name} does not implement ITenantAccessValidator<{typeof(TKey).Name}>, the access " +
                $"validator of this application's tenant key type '{typeof(TKey).Name}'.");
        }

        TenantResolutionMiddlewareConfigurator<TKey>.Register(tenant.Services);
        tenant.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ITenantAccessValidator<TKey>), typeof(TValidator)));
    }
}
