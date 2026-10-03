namespace Tenantry.Http.Internal;

/// <summary>
/// Adds the current tenant's id to a client's requests, in <see cref="TenantPropagation.HeaderName"/>, when a tenant is
/// current and the request is for the client's own service (<see cref="PropagationTarget"/>). A header already on the
/// request that names another tenant is refused. With no tenant, the request goes as the caller made it, and the
/// receiving service decides.
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
        if (source.CurrentTenantId is not { } tenantId || !target.Allows(request.RequestUri))
        {
            return;
        }

        // A header put there before (forwarded from the incoming request, or a client's DefaultRequestHeaders) would call
        // the service as whatever tenant it names, with this service's credentials.
        if (request.Headers.TryGetValues(TenantPropagation.HeaderName, out var existing))
        {
            if (existing.All(value => value == tenantId))
                return;

            throw new InvalidOperationException(
                $"The request already carries the {TenantPropagation.HeaderName} header with " +
                $"'{string.Join("', '", existing)}', but the current tenant is '{tenantId}'. Remove the header from the " +
                "request (header propagation, copied incoming headers or the client's DefaultRequestHeaders); to call " +
                "as another tenant, make it current with ITenantContextSetter.Use.");
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
