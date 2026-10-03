using Microsoft.Extensions.Http;

namespace Tenantry.Http.Internal;

/// <summary>
/// The service one client's requests carry the tenant to: the origin of the base address the client's configuration
/// gives it. A client without one there (a gRPC client, which takes its address elsewhere, or a typed client that sets
/// it in its constructor) carries the tenant on every request.
/// </summary>
internal sealed class PropagationTarget(Uri? baseAddress)
{
    /// <summary>
    /// The target of a client with <paramref name="options"/>: its configuration (<c>AddHttpClient(c =&gt; …)</c>,
    /// <c>ConfigureHttpClient</c>) applied to an HttpClient that never sends, to read the base address it sets.
    /// </summary>
    public static PropagationTarget Of(HttpClientFactoryOptions options)
    {
        if (options.HttpClientActions.Count == 0)
            return new PropagationTarget(null);

        using HttpClient probe = new(new NeverSends());

        foreach (var action in options.HttpClientActions)
            action(probe);

        return new PropagationTarget(probe.BaseAddress is { IsAbsoluteUri: true } address ? address : null);
    }

    public bool Allows(Uri? requestUri) =>
        baseAddress is null ||
        requestUri is { IsAbsoluteUri: true } &&
        Uri.Compare(requestUri, baseAddress, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private sealed class NeverSends : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe that reads a client's base address sends nothing.");
    }
}
