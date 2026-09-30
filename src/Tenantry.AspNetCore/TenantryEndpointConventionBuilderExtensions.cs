using Tenantry.AspNetCore;

// Extensions on IEndpointConventionBuilder live in its namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
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
    public static TBuilder AllowMissingTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new AllowMissingTenantAttribute());
        return builder;
    }
}
