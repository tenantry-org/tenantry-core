using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Non-generic interface that enables <c>UseTenantry()</c> without repeating the TKey type parameter.
/// Registered, closed over the tenant key type, by the first ASP.NET Core feature added to the builder.
/// </summary>
internal interface ITenantResolutionMiddlewareConfigurator
{
    IApplicationBuilder Use(IApplicationBuilder app);
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
    }

    // Checks the registration when the pipeline is built, so a web application fails as it starts.
    public IApplicationBuilder Use(IApplicationBuilder app)
    {
        var services = app.ApplicationServices;

        if (!services.GetServices<ITenantResolver>().Any())
        {
            throw new InvalidOperationException(
                $"app.UseTenantry() has no tenant resolvers for tenant key type '{typeof(TKey).Name}'. Add at least one " +
                "in AddTenantry, such as tenant.ResolveFromHeader(...), tenant.ResolveFromRouteValue(...), " +
                "tenant.ResolveFromClaim(...), tenant.ResolveFromSubdomain() or tenant.UseResolver(...).");
        }

        // Checked without resolving the store, which may be scoped and create a DbContext.
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantStore<TKey>)) == false)
        {
            throw new InvalidOperationException(
                $"app.UseTenantry() has no tenant store for ITenantStore<{typeof(TKey).Name}>. Register one in " +
                "AddTenantry, with tenant.UseStore<TStore>() or tenant.UseInMemoryStore(...).");
        }

        return app.UseMiddleware<TenantResolutionMiddleware<TKey>>();
    }
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
