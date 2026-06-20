using Microsoft.AspNetCore.Builder;
using Tenantry.AspNetCore.Attributes;

namespace Tenantry.AspNetCore.Extensions;

/// <summary>
/// Extension methods for applying Tenantry endpoint metadata.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Requires Tenantry to resolve a tenant for the endpoint.
        /// </summary>
        public TBuilder RequireTenant()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithMetadata(new RequireTenantAttribute());
            return builder;
        }

        /// <summary>
        /// Allows the endpoint to execute without a resolved tenant, even when tenant resolution is required by default.
        /// </summary>
        public TBuilder AllowMissingTenant()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithMetadata(new AllowMissingTenantAttribute());
            return builder;
        }
    }
}
