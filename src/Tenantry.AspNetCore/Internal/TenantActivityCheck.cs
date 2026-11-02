using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Checks again that the tenant of a long-lived connection (a SignalR connection, a Blazor Server circuit) still
/// exists and is active. The middleware checked it once, when the request that opened the connection arrived.
/// </summary>
internal static class TenantActivityCheck
{
    /// <summary>
    /// Does nothing without a current tenant. Otherwise looks the tenant up again, through the cache with
    /// <c>CacheTenants</c>, and throws <see cref="TenantNotFoundException"/> if the store no longer has it, or
    /// <see cref="TenantInactiveException"/> if an activity validator refuses it.
    /// </summary>
    /// <param name="services">The services of the call.</param>
    /// <param name="registration">
    /// The Tenantry method that added the check, for the error without <c>AddTenantry</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup and the check.</param>
    public static ValueTask ThrowIfInactiveAsync(
        IServiceProvider services,
        string registration,
        CancellationToken cancellationToken) =>
        KeyType(services, registration).Accept(new Check(services, expected: null, cancellationToken));

    /// <summary>
    /// Checks the current tenant as <see cref="ThrowIfInactiveAsync(IServiceProvider, string, CancellationToken)"/>
    /// does, after checking that it is <paramref name="expected"/>, the tenant a Blazor Server circuit opened with.
    /// Throws <see cref="TenantNotResolvedException"/> when the circuit has a tenant and none is current, and
    /// <see cref="InvalidOperationException"/> when another tenant is current, or when one is and the circuit has
    /// none.
    /// </summary>
    /// <param name="services">The services of the call.</param>
    /// <param name="registration">
    /// The Tenantry method that added the check, for the error without <c>AddTenantry</c>.
    /// </param>
    /// <param name="expected">
    /// The circuit's tenant, from <see cref="CurrentTenant"/>, or <see langword="null"/> for a circuit without one.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup and the check.</param>
    public static ValueTask ThrowIfNotCurrentOrInactiveAsync(
        IServiceProvider services,
        string registration,
        ITenantDescriptor? expected,
        CancellationToken cancellationToken) =>
        KeyType(services, registration).Accept(new Check(services, new(expected), cancellationToken));

    /// <summary>The tenant current now, or <see langword="null"/>.</summary>
    /// <param name="services">The services of the call.</param>
    /// <param name="registration">
    /// The Tenantry method that added the check, for the error without <c>AddTenantry</c>.
    /// </param>
    public static ITenantDescriptor? CurrentTenant(IServiceProvider services, string registration) =>
        KeyType(services, registration).Accept(new Current(services));

    private static ITenantKeyType KeyType(IServiceProvider services, string registration) =>
        services.GetService<ITenantKeyType>() ?? throw new InvalidOperationException(
            $"{registration} checks the tenant of each call, but Tenantry is not registered. Register it with " +
            "builder.Services.AddTenantry<TKey>(...).");

    // The tenant a check expects to be current; Tenant is null for a circuit opened without one.
    private sealed record ExpectedTenant(ITenantDescriptor? Tenant);

    private sealed class Check(IServiceProvider services, ExpectedTenant? expected, CancellationToken cancellationToken)
        : ITenantKeyTypeVisitor<ValueTask>
    {
        public ValueTask Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            var tenant = services.GetRequiredService<ITenantContext<TKey>>().CurrentTenant;

            if (expected is not null)
            {
                ThrowIfNotExpected((ITenantDescriptor<TKey>?)expected.Tenant, tenant);
            }

            return tenant is null ? ValueTask.CompletedTask : RefuseIfInactiveAsync(tenant);
        }

        private static void ThrowIfNotExpected<TKey>(ITenantDescriptor<TKey>? expected, ITenantDescriptor<TKey>? tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            if (expected is not null && tenant is null)
            {
                throw new TenantNotResolvedException(
                    $"This Blazor Server circuit opened for tenant '{expected.TenantId}', but the connection it now " +
                    "uses identifies no tenant, so the circuit ends. The request that reopened the connection resolved " +
                    "no tenant, as it does for a tenant that is no longer active.");
            }

            if (tenant is not null && (expected is null || !expected.TenantId.Equals(tenant.TenantId)))
            {
                throw new InvalidOperationException(
                    (expected is null
                        ? "This Blazor Server circuit opened without a tenant"
                        : $"This Blazor Server circuit opened for tenant '{expected.TenantId}'") +
                    $", but the connection it now uses identifies tenant '{tenant.TenantId}', so the circuit ends. " +
                    "A circuit keeps the tenant it opened with.");
            }
        }

        private async ValueTask RefuseIfInactiveAsync<TKey>(ITenantDescriptor<TKey> tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            var current = await services.GetRequiredService<ITenantLookup<TKey>>()
                              .GetTenantAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false)
                          ?? throw new TenantNotFoundException(tenant.TenantId);

            await services.GetRequiredService<ITenantActivity<TKey>>()
                .ThrowIfInactiveAsync(current, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Current(IServiceProvider services) : ITenantKeyTypeVisitor<ITenantDescriptor?>
    {
        public ITenantDescriptor? Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey> =>
            services.GetRequiredService<ITenantContext<TKey>>().CurrentTenant;
    }
}
