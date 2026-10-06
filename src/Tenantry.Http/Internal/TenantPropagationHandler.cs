using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tenantry.Http.Internal;

/// <summary>
/// Adds the current tenant's id to a client's requests, in <see cref="TenantPropagation.HeaderName"/>, when a tenant is
/// current and the request is for the client's own service (<see cref="PropagationTarget"/>). A header already on a
/// request to that service is refused unless it names the current tenant. With no tenant, the request goes without the
/// header, and the receiving service decides.
/// </summary>
internal sealed class TenantPropagationHandler(ITenantHeaderSource source, PropagationTarget target) : DelegatingHandler
{
    /// <summary>The handler of the client <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <c>AddHttpPropagation()</c> was not called, or the client has no address to send the tenant to.
    /// </exception>
    public static TenantPropagationHandler Create(IServiceProvider provider, string name, Uri? serviceAddress) =>
        new(
            provider.GetService<ITenantHeaderSource>() ?? throw new InvalidOperationException(
                $"The HTTP client '{name}' calls UseTenantry(), but Tenantry is not set up to send tenants: add " +
                "tenant.AddHttpPropagation() in AddTenantry."),
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
        if (!target.Allows(request.RequestUri))
        {
            return;
        }

        var tenantId = source.CurrentTenantId;

        // A header put there before (forwarded from the incoming request, or a client's DefaultRequestHeaders) would call
        // the service as whatever tenant it names, with this service's credentials, whether or not a tenant is current.
        if (request.Headers.TryGetValues(TenantPropagation.HeaderName, out var headers))
        {
            var existing = headers.ToArray();

            if (tenantId is not null && existing.All(value => value == tenantId))
                return;

            var current = tenantId is null ? "no tenant is current" : $"the current tenant is '{tenantId}'";

            throw new InvalidOperationException(
                $"The request already carries the {TenantPropagation.HeaderName} header with " +
                $"'{string.Join("', '", existing)}', but {current}. Remove the header from the request (header " +
                "propagation, copied incoming headers or the client's DefaultRequestHeaders); to call as a tenant, make " +
                "it current with ITenantContextSetter.MakeCurrent or ITenantScopeFactory.RunInScopeAsync.");
        }

        if (tenantId is null)
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
