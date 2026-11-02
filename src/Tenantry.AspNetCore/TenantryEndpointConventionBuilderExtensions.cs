using Tenantry.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for applying Tenantry endpoint metadata.
/// </summary>
public static class TenantryEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Requires Tenantry to resolve a tenant for the endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, or group of endpoints, to configure.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// With both this and <c>AllowMissingTenant</c> on an endpoint, the one added last wins. On
    /// <c>MapControllers()</c> or <c>MapRazorPages()</c>, it applies to the actions and pages without a
    /// <see cref="RequireTenantAttribute"/> or <see cref="AllowMissingTenantAttribute"/> on their controller, action
    /// or page, which wins over it.
    /// </remarks>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new RequireTenantAttribute());
        return builder;
    }

    /// <summary>
    /// Allows the endpoint to execute without a resolved tenant, even when tenant resolution is required by default.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, or group of endpoints, to configure.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// With both this and <c>RequireTenant</c> on an endpoint, the one added last wins. On
    /// <c>MapControllers()</c> or <c>MapRazorPages()</c>, it applies to the actions and pages without a
    /// <see cref="RequireTenantAttribute"/> or <see cref="AllowMissingTenantAttribute"/> on their controller, action
    /// or page, which wins over it.
    /// </remarks>
    public static TBuilder AllowMissingTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new AllowMissingTenantAttribute());
        return builder;
    }
}
