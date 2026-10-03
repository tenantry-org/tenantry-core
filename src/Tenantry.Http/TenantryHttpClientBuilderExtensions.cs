using Microsoft.Extensions.Http;
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
    /// <c>ResolveFromPropagationHeader()</c>. Requires <c>tenant.AddHttpPropagation()</c> in <c>AddTenantry</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request carries the header when a tenant is current and the caller has not set the header itself. A request
    /// without a tenant goes without it, and the receiving service decides what that means, for example with
    /// <c>RequireTenant()</c>.
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

        PropagationTarget target = new();

        // After the client's own configuration, which sets its base address.
        builder.Services.PostConfigure<HttpClientFactoryOptions>(
            name,
            options => options.HttpClientActions.Add(client => target.Record(client.BaseAddress)));

        return builder.AddHttpMessageHandler(sp => new TenantPropagationHandler(
            sp.GetService<ITenantHeaderSource>() ?? throw new InvalidOperationException(
                $"The HTTP client '{name}' calls UseTenantry(), but Tenantry is not set up to send tenants: add " +
                "tenant.AddHttpPropagation() in AddTenantry."),
            target));
    }
}
