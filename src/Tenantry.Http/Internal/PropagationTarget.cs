namespace Tenantry.Http.Internal;

/// <summary>
/// The service one client's requests carry the tenant to: the origin of the client's base address, recorded each time
/// the factory creates the client, after the client's own configuration has set it. A client without a base address
/// (a gRPC client, which takes its address elsewhere) carries it on every request.
/// </summary>
internal sealed class PropagationTarget
{
    private volatile Uri? _baseAddress;

    public void Record(Uri? baseAddress) => _baseAddress = baseAddress is { IsAbsoluteUri: true } ? baseAddress : null;

    public bool Allows(Uri? requestUri)
    {
        var baseAddress = _baseAddress;

        return baseAddress is null ||
               requestUri is { IsAbsoluteUri: true } &&
               Uri.Compare(
                   requestUri,
                   baseAddress,
                   UriComponents.SchemeAndServer,
                   UriFormat.SafeUnescaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }
}
