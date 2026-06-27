using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenantry.AspNetCore.Resolution;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Internal;

internal sealed class TenantConfigurationValidatorHostedService<TKey> : IHostedService
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<ITenantResolver> _resolvers;

    public TenantConfigurationValidatorHostedService(
        IServiceProvider serviceProvider,
        IEnumerable<ITenantResolver> resolvers)
    {
        _serviceProvider = serviceProvider;
        _resolvers = resolvers;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_resolvers.Any())
        {
            throw new InvalidOperationException(
                $"Tenantry is misconfigured for tenant key type '{typeof(TKey).Name}': no tenant resolvers were registered. " +
                $"Add at least one resolver such as tenant.ResolveFromHeader(...), tenant.ResolveFromRouteValue(...), " +
                $"tenant.ResolveFromClaim(...), tenant.ResolveFromSubdomain(), or tenant.UseResolver(...).");
        }

        // Check that a store is REGISTERED without resolving (and therefore constructing) it. A custom
        // store may be scoped and EF-backed, so instantiating it here just to null-check would create a
        // DbContext as a startup side effect. IServiceProviderIsService reports registration presence for
        // the closed generic without activating anything.
        var isService = _serviceProvider.GetRequiredService<IServiceProviderIsService>();

        if (!isService.IsService(typeof(ITenantStore<TKey>)))
        {
            throw new InvalidOperationException(
                $"Tenantry is misconfigured for tenant key type '{typeof(TKey).Name}': no tenant store was registered. " +
                $"Add a tenant store such as tenant.UseInMemoryStore(...), tenant.UseStore<TStore>(), or tenant.UseStore(...).");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
