using Microsoft.Extensions.Http;

namespace Tenantry.Http.Internal;

/// <summary>
/// The service one client's requests carry the tenant to: the origin of the address given to <c>UseTenantry</c>, or else
/// of the base address the client's configuration gives it.
/// </summary>
internal sealed class PropagationTarget(Uri address)
{
    /// <summary>
    /// The target of the client <paramref name="name"/>: <paramref name="serviceAddress"/>, or else its configuration
    /// (<c>AddHttpClient(c =&gt; …)</c>, <c>ConfigureHttpClient</c>) applied to an HttpClient that never sends, to read
    /// the base address it sets.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is neither.</exception>
    public static PropagationTarget Of(string name, HttpClientFactoryOptions options, Uri? serviceAddress)
    {
        if (serviceAddress is not null)
            return new PropagationTarget(serviceAddress);

        using HttpClient probe = new(new NeverSends());

        foreach (var action in options.HttpClientActions)
            action(probe);

        // Without an address, the header would go to any host a request names.
        return probe.BaseAddress is { IsAbsoluteUri: true } address
            ? new PropagationTarget(address)
            : throw new InvalidOperationException(
                $"The HTTP client '{name}' calls UseTenantry(), but its registration sets no absolute BaseAddress, so " +
                "Tenantry cannot tell which service may receive the tenant. Set BaseAddress where the client is " +
                "registered (AddHttpClient(c => c.BaseAddress = …)), or pass the service's address to " +
                "UseTenantry(address), as for a gRPC client or a typed client that sets BaseAddress in its constructor.");
    }

    public bool Allows(Uri? requestUri) =>
        requestUri is { IsAbsoluteUri: true } &&
        Uri.Compare(requestUri, address, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private sealed class NeverSends : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe that reads a client's base address sends nothing.");
    }
}
