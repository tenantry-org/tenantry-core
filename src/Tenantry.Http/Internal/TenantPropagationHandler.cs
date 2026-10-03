namespace Tenantry.Http.Internal;

/// <summary>
/// Adds the current tenant's id to a client's requests, in <see cref="TenantPropagation.HeaderName"/>: when a tenant is
/// current, the request is for the client's own service (<see cref="PropagationTarget"/>), and the caller has not set
/// the header itself. With no tenant, the request goes without it, and the receiving service decides.
/// </summary>
internal sealed class TenantPropagationHandler(ITenantHeaderSource source, PropagationTarget target) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AddTenant(request);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AddTenant(request);
        return base.Send(request, cancellationToken);
    }

    private void AddTenant(HttpRequestMessage request)
    {
        if (source.CurrentTenantId is not { } tenantId ||
            request.Headers.Contains(TenantPropagation.HeaderName) ||
            !target.Allows(request.RequestUri))
        {
            return;
        }

        ThrowIfNotSendable(tenantId);
        request.Headers.TryAddWithoutValidation(TenantPropagation.HeaderName, tenantId);
    }

    // A header value goes as printable ASCII, and the receiver trims it: an id it would read differently fails here,
    // rather than as an encoding error in the client or another tenant's id at the service.
    private static void ThrowIfNotSendable(string tenantId)
    {
        if (tenantId.Length == 0 || tenantId[0] == ' ' || tenantId[^1] == ' ' ||
            tenantId.AsSpan().ContainsAnyExceptInRange(' ', '~'))
        {
            throw new InvalidOperationException(
                $"The current tenant's id, '{tenantId}', cannot be sent in the {TenantPropagation.HeaderName} header: " +
                "an HTTP header carries printable ASCII characters, with no space at either end. Give tenants ids of " +
                "that form (a GUID, a number or a slug) to call other services as them.");
        }
    }
}
