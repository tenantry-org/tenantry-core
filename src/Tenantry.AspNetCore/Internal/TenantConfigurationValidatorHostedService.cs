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

        using var scope = _serviceProvider.CreateScope();

        if (scope.ServiceProvider.GetService<ITenantStore<TKey>>() is null)
        {
            throw new InvalidOperationException(
                $"Tenantry is misconfigured for tenant key type '{typeof(TKey).Name}': no tenant store was registered. " +
                $"Add a tenant store such as tenant.UseInMemoryStore(...), tenant.UseStore<TStore>(), or tenant.UseStore(...).");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
