using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tenantry.Http.Internal;

/// <summary>
/// Adds the current tenant's id to a client's requests, in <see cref="TenantPropagation.HeaderName"/>, when a tenant is
/// current and the request is for the client's own service (<see cref="PropagationTarget"/>, as
/// <see cref="TargetCheck"/> recorded it). A header already on a request to that service is refused unless it names
/// the current tenant, and is then sent in the form <see cref="TenantIds.Format{TKey}"/> writes. With no tenant, the
/// request goes without the header, and the receiving service decides.
/// </summary>
internal sealed class TenantPropagationHandler(ITenantHeaderSource source, PropagationTarget target) : DelegatingHandler
{
    /// <summary>The service the client's requests carry the tenant to.</summary>
    public PropagationTarget Target => target;

    /// <summary>The handler of the client <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// Tenantry is not registered, or the client has no address to send the tenant to.
    /// </exception>
    public static TenantPropagationHandler Create(IServiceProvider provider, string name, Uri? serviceAddress) =>
        new(
            provider.GetRequiredService<ITenantHeaderSource>(),
            PropagationTarget.Of(name, provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name), serviceAddress));

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
        if (!IsForTarget(request))
        {
            return;
        }

        var tenantId = source.CurrentTenantId;

        // A header put there before (forwarded from the incoming request, or a client's DefaultRequestHeaders) would call
        // the service as whatever tenant it names, with this service's credentials, whether or not a tenant is current.
        if (request.Headers.TryGetValues(TenantPropagation.HeaderName, out var headers))
        {
            var existing = headers.ToArray();

            // Compared as the service reads the id, so a forwarded 3F2504E0-… names the tenant 3f2504e0-….
            if (tenantId is null || !existing.All(source.NamesCurrentTenant))
            {
                var current = tenantId is null ? "no tenant is current" : $"the current tenant is '{tenantId}'";

                throw new InvalidOperationException(
                    $"The request already carries the {TenantPropagation.HeaderName} header with " +
                    $"'{string.Join("', '", existing)}', but {current}. Remove the header from the request " +
                    "(header propagation, copied incoming headers or the client's DefaultRequestHeaders); to call as " +
                    "a tenant, make it current with ITenantContextSetter.MakeCurrent or " +
                    "ITenantScopeFactory.RunInScopeAsync.");
            }

            // Replaced by one value in the form Tenantry writes: the service reads a header repeated as no tenant.
            request.Headers.Remove(TenantPropagation.HeaderName);
        }

        if (tenantId is null)
        {
            return;
        }

        ThrowIfNotSendable(tenantId);
        request.Headers.TryAddWithoutValidation(TenantPropagation.HeaderName, tenantId);
    }

    // A handler between TargetCheck and this one may send a copy of the request, without the record: its address is
    // then the one to check.
    private bool IsForTarget(HttpRequestMessage request) =>
        request.Options.TryGetValue(TargetCheck.IsForTarget, out var isForTarget)
            ? isForTarget
            : target.Allows(request.RequestUri);

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
