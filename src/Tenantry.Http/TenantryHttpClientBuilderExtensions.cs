using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Tenantry;
using Tenantry.Http.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Sends the current tenant with an HTTP or gRPC client's requests.
/// </summary>
public static class TenantryHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds the current tenant's id to the client's requests, in the <see cref="TenantPropagation.HeaderName"/> header,
    /// formatted by <see cref="TenantIds.Format{TKey}"/>, for the service it calls to resolve with Tenantry.AspNetCore's
    /// <c>ResolveFromPropagationHeader(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires <c>tenant.AddHttpPropagation()</c> in <c>AddTenantry</c>. A request that already carries the header
    /// with another tenant's id (forwarded from an incoming request, or from the client's <c>DefaultRequestHeaders</c>)
    /// throws
    /// <see cref="InvalidOperationException"/>. With no tenant current, the request goes as the caller built it, and
    /// the receiving service decides what a missing header means, for example with <c>RequireTenant()</c>.
    /// </para>
    /// <para>
    /// The header goes only to the scheme, host and port of <paramref name="serviceAddress"/>, or else of the base
    /// address set in the client's registration (<c>AddHttpClient(c =&gt; c.BaseAddress = …)</c>), not to an absolute
    /// address elsewhere. A redirect the client follows keeps it. The id must be printable ASCII with no space at
    /// either end.
    /// </para>
    /// </remarks>
    /// <param name="builder">The client's builder, from <c>AddHttpClient</c> or <c>AddGrpcClient</c>.</param>
    /// <param name="serviceAddress">
    /// The address of the service the client calls, for a client whose registration sets no <c>BaseAddress</c>: a gRPC
    /// client's <c>Address</c>, or the address a typed client sets in its constructor. Only its scheme, host and port
    /// are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="builder"/> is <c>ConfigureHttpClientDefaults</c>'s, which configures every client, third-party
    /// SDKs' included. The client is created without <c>tenant.AddHttpPropagation()</c>, or with neither
    /// <paramref name="serviceAddress"/> nor an absolute <c>BaseAddress</c> in its registration (thrown when it is
    /// created).
    /// A request sent while a tenant is current already carries the header with another tenant's id, or the tenant's
    /// id is not printable ASCII without a space at either end (thrown when the request is sent).
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="serviceAddress"/> is not absolute.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddHttpClient&lt;BillingClient&gt;(c =&gt; c.BaseAddress = new Uri("https://billing.internal"))
    ///     .UseTenantry();
    ///
    /// var inventory = new Uri("https://inventory.internal");
    /// builder.Services.AddGrpcClient&lt;Inventory.InventoryClient&gt;(o =&gt; o.Address = inventory)
    ///     .UseTenantry(inventory);
    /// </code>
    /// </example>
    public static IHttpClientBuilder UseTenantry(this IHttpClientBuilder builder, Uri? serviceAddress = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (serviceAddress is { IsAbsoluteUri: false })
        {
            throw new ArgumentException(
                $"The service's address, '{serviceAddress}', must be absolute, such as https://inventory.internal.",
                nameof(serviceAddress));
        }

        // ConfigureHttpClientDefaults' builder has no name: it configures every client in the application.
        if (builder.Name is not { } name)
        {
            throw new InvalidOperationException(
                "UseTenantry() sends the tenant to the service one client calls, so it cannot go in " +
                "ConfigureHttpClientDefaults, which configures every client, third-party SDKs' included. Call it on " +
                "the clients of your own services: builder.Services.AddHttpClient<BillingClient>(…).UseTenantry().");
        }

        // Read when the factory builds the client's handlers, not added to HttpClientActions: gRPC's client factory
        // refuses those (before Grpc.Net.ClientFactory 2.64) or warns about them each time it creates a client.
        return builder.AddHttpMessageHandler(sp => new TenantPropagationHandler(
            sp.GetService<ITenantHeaderSource>() ?? throw new InvalidOperationException(
                $"The HTTP client '{name}' calls UseTenantry(), but Tenantry is not set up to send tenants: add " +
                "tenant.AddHttpPropagation() in AddTenantry."),
            PropagationTarget.Of(name, sp.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name), serviceAddress)));
    }
}
