using Tenantry;
using Tenantry.Options;

// Extensions on the Tenantry builder live in the DI namespace, so registration code needs no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Options with values per tenant.
/// </summary>
public static class TenantryOptionsTenantBuilderExtensions
{
    /// <summary>
    /// Configures options per tenant: in <paramref name="configure"/>, each <c>Configure&lt;TOptions&gt;</c> or
    /// <c>ConfigureAll&lt;TOptions&gt;</c> sets what differs for each tenant, and <c>IOptionsSnapshot&lt;TOptions&gt;</c>
    /// and <c>IOptionsMonitor&lt;TOptions&gt;</c> then give the current tenant's value. <c>IOptions&lt;TOptions&gt;</c>
    /// always gives the ordinary value. See <see cref="TenantOptionsBuilder{TKey}"/>.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Names the options types and sets their values per tenant.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.Configure&lt;BrandingOptions&gt;(builder.Configuration.GetSection("Branding"));   // the defaults
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .ConfigurePerTenant(perTenant =&gt; perTenant
    ///         .Configure&lt;BrandingOptions&gt;((options, t) =&gt; options.Colour = t.As&lt;AppTenant&gt;().BrandColour)
    ///         .Configure&lt;JwtBearerOptions&gt;(JwtBearerDefaults.AuthenticationScheme, (options, t) =&gt;
    ///             options.Authority = t.As&lt;AppTenant&gt;().Authority)));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> ConfigurePerTenant<TKey>(this ITenantBuilder<TKey> builder, Action<TenantOptionsBuilder<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new TenantOptionsBuilder<TKey>(builder.Services));
        return builder;
    }
}
