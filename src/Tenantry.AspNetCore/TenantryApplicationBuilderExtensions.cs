using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Internal;

// Extensions on IApplicationBuilder live in its namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds Tenantry's tenant resolution to the request pipeline.
/// </summary>
public static class TenantryApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the tenant resolution middleware to the pipeline.
    /// Must be called after authentication middleware (<c>app.UseAuthentication()</c>)
    /// if using claim-based resolution, and before any middleware that requires
    /// a resolved tenant (e.g. authorisation, controllers).
    /// When using <c>RequireTenant()</c> or <c>AllowMissingTenant()</c> endpoint metadata,
    /// ensure routing has executed before this middleware. <see cref="WebApplication"/>
    /// handles this automatically for minimal APIs and controllers.
    /// </summary>
    /// <param name="app">The application's request pipeline.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Tenantry is not registered with a way to resolve requests (a <c>ResolveFrom…</c> or <c>UseResolver</c>
    /// method in <c>AddTenantry</c>), or no tenant store is registered.
    /// </exception>
    public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var configurator = app.ApplicationServices.GetService<ITenantResolutionMiddlewareConfigurator>()
            ?? throw new InvalidOperationException(
                "app.UseTenantry() found no tenant resolution to run. Register Tenantry with a way to resolve requests " +
                "to tenants before building the application, for example builder.Services.AddTenantry<Guid>(tenant => " +
                "tenant.ResolveFromHeader(\"X-Tenant-Id\").UseStore<AppTenantStore>()), or another ResolveFrom... or " +
                "UseResolver method.");

        return configurator.Use(app);
    }
}
