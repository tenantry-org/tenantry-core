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
    /// A request that already carries the header (forwarded from an incoming request, or from the client's
    /// <c>DefaultRequestHeaders</c>) throws <see cref="InvalidOperationException"/>, unless it names the current tenant.
    /// Call <c>UseTenantry()</c> after <c>AddHeaderPropagation()</c> and after any handler that sets headers: a handler
    /// added after it sets headers Tenantry does not see. With no tenant current, the request goes without the header,
    /// and the receiving service decides what that means, for example with <c>RequireTenant()</c>.
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
    /// The address of the service the client calls, for a client whose registration sets no <c>BaseAddress</c>. A gRPC
    /// client keeps its <c>Address</c> in its own options, which Tenantry cannot read, and a typed client may set its
    /// address in its constructor, after the handlers are built. Only its scheme, host and port are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="builder"/> is <c>ConfigureHttpClientDefaults</c>'s, which configures every client, third-party
    /// SDKs' included. The client has neither <paramref name="serviceAddress"/> nor an absolute <c>BaseAddress</c> in
    /// its registration (thrown as the host starts, or, in a service provider built without a host, when the client is
    /// created). Tenantry is not registered with <c>AddTenantry</c> (thrown as the host starts, or, in a service
    /// provider built without a host, when the client is created).
    /// A request already carries the header with an id other than the current tenant's, or with no tenant current, or
    /// the tenant's id is not printable ASCII without a space at either end (thrown when the request is sent).
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

        TenantHeaderSource.Register(builder.Services);
        PropagationCheck.Register(builder.Services, name, serviceAddress);

        // Read when the factory builds the client's handlers, not added to HttpClientActions: gRPC's client factory
        // refuses those (before Grpc.Net.ClientFactory 2.64) or warns about them each time it creates a client.
        return builder.AddHttpMessageHandler(sp => TenantPropagationHandler.Create(sp, name, serviceAddress));
    }
}
