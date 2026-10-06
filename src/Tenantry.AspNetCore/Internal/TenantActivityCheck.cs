using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Checks again that the tenant current on a long-lived connection (a SignalR connection, a Blazor Server circuit)
/// still exists and is active. The middleware checked it once, when the request that opened the connection arrived,
/// and the tenant stays current for the connection's lifetime.
/// </summary>
internal static class TenantActivityCheck
{
    /// <summary>
    /// Does nothing without a current tenant. Otherwise looks the tenant up again, through the cache with
    /// <c>CacheTenants</c>, and throws <see cref="TenantNotFoundException"/> if the store no longer has it, or
    /// <see cref="TenantInactiveException"/> if an activity validator refuses it.
    /// </summary>
    /// <param name="services">The services of the call.</param>
    /// <param name="registration">The Tenantry method that added the check, for the error without <c>AddTenantry</c>.</param>
    /// <param name="cancellationToken">Cancels the lookup and the check.</param>
    public static ValueTask ThrowIfInactiveAsync(
        IServiceProvider services,
        string registration,
        CancellationToken cancellationToken)
    {
        var keyType = services.GetService<ITenantKeyType>() ?? throw new InvalidOperationException(
            $"{registration} checks the tenant of each call, but Tenantry is not registered. Register it with " +
            "builder.Services.AddTenantry<TKey>(...).");

        return keyType.Accept(new Check(services, cancellationToken));
    }

    private sealed class Check(IServiceProvider services, CancellationToken cancellationToken) : ITenantKeyTypeVisitor<ValueTask>
    {
        public ValueTask Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey> =>
            services.GetRequiredService<ITenantContext<TKey>>().CurrentTenant is { } tenant
                ? ThrowIfInactiveAsync(tenant)
                : ValueTask.CompletedTask;

        private async ValueTask ThrowIfInactiveAsync<TKey>(ITenantDescriptor<TKey> tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            var current = await services.GetRequiredService<ITenantLookup<TKey>>()
                              .GetTenantAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false)
                          ?? throw new TenantNotFoundException(tenant.TenantId);

            await services.GetRequiredService<ITenantActivity<TKey>>()
                .ThrowIfInactiveAsync(current, cancellationToken).ConfigureAwait(false);
        }
    }
}
