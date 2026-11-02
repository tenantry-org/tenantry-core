namespace Tenantry.Http.Internal;

/// <summary>
/// The outermost handler of a client with <c>UseTenantry()</c>: records whether a request is for the client's service
/// (<see cref="PropagationTarget"/>) from the address it was sent to, before a handler replaces that address.
/// <see cref="TenantPropagationHandler"/> runs later, after header propagation, and reads the record.
/// </summary>
/// <remarks>
/// Service discovery (.NET Aspire's <c>https+http://billing</c>) replaces the address with the endpoint it resolves,
/// whose host and port are not known in advance, in a handler <c>ConfigureHttpClientDefaults</c> adds outside the
/// client's own.
/// </remarks>
internal sealed class TargetCheck(PropagationTarget target) : DelegatingHandler
{
    /// <summary>Whether the request is for the client's service, as its address read before any handler ran.</summary>
    public static readonly HttpRequestOptionsKey<bool> IsForTarget = new("Tenantry.Http.IsForTarget");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Record(request);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Record(request);
        return base.Send(request, cancellationToken);
    }

    private void Record(HttpRequestMessage request) => request.Options.Set(IsForTarget, target.Allows(request.RequestUri));
}
