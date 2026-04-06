using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Extensions;

/// <summary>
/// Extension methods for adding TenantKit middleware to the request pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
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
    public static IApplicationBuilder UseTenantry(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var configurator = app.ApplicationServices.GetRequiredService<ITenantResolutionMiddlewareConfigurator>();
        return configurator.Use(app);
    }
}
