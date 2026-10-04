using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Http.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Sends the current tenant to the services an application calls over HTTP or gRPC.
/// </summary>
public static class TenantryHttpTenantBuilderExtensions
{
    /// <summary>
    /// Lets the HTTP and gRPC clients marked with <see cref="TenantryHttpClientBuilderExtensions.UseTenantry"/> send
    /// the current tenant's id to the services they call, in the <see cref="TenantPropagation.HeaderName"/> header.
    /// The receiving service reads it with Tenantry.AspNetCore's <c>ResolveFromPropagationHeader(...)</c>.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .AddHttpPropagation());
    ///
    /// builder.Services.AddHttpClient&lt;BillingClient&gt;(c =&gt; c.BaseAddress = new Uri("https://billing.internal"))
    ///     .UseTenantry();
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> AddHttpPropagation<TKey>(this ITenantBuilder<TKey> builder)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ITenantHeaderSource>(sp =>
            new TenantHeaderSource<TKey>(sp.GetRequiredService<ITenantContext<TKey>>()));

        return builder;
    }
}
