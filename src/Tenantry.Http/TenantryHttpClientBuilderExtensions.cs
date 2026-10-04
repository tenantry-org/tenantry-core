using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Tenantry;
using Tenantry.Http.Internal;

// Extensions on IHttpClientBuilder live in the DI namespace, beside AddHttpClient, so they need no using directive.
// ReSharper disable once CheckNamespace
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
    /// Requires <c>tenant.AddHttpPropagation()</c> in <c>AddTenantry</c>. While a tenant is current, a request carries the tenant's id in the header. A request that already carries the
    /// header with another tenant's id (forwarded from an incoming request, or from the client's
    /// <c>DefaultRequestHeaders</c>) throws <see cref="InvalidOperationException"/>. With no tenant current, the
    /// request goes as the caller built it, and the receiving service decides what a missing header means, for example
    /// with <c>RequireTenant()</c>.
    /// </para>
    /// <para>
    /// The header goes only to the service the client is for: when the client has a base address, set in its
    /// configuration (<c>AddHttpClient(c =&gt; c.BaseAddress = …)</c>), only requests to that scheme, host and port
    /// carry it; a request to an absolute address elsewhere does not. A client with no base address there (a gRPC
    /// client, or a typed client that sets it in its constructor) carries it on every request. A redirect that the
    /// client follows keeps the request's headers, this one included.
    /// </para>
    /// <para>
    /// The id must be printable ASCII with no space at either end, as a header carries it; a request as a tenant whose
    /// id is not throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The client's builder, from <c>AddHttpClient</c> or <c>AddGrpcClient</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="builder"/> is <c>ConfigureHttpClientDefaults</c>'s, which configures every client, third-party
    /// SDKs' included. The client is created without <c>tenant.AddHttpPropagation()</c> (thrown when it is created).
    /// A request sent while a tenant is current already carries the header with another tenant's id, or the tenant's
    /// id is not printable ASCII without a space at either end (thrown when the request is sent).
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddHttpClient&lt;BillingClient&gt;(c =&gt; c.BaseAddress = new Uri("https://billing.internal"))
    ///     .UseTenantry();
    /// </code>
    /// </example>
    public static IHttpClientBuilder UseTenantry(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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
            PropagationTarget.Of(sp.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name))));
    }
}
