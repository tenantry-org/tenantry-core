using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// The <see cref="ITenantBuilder{TKey}"/> that <c>AddTenantry</c> passes to its configuration callback.
/// </summary>
internal sealed class TenantBuilder<TKey>(IServiceCollection services) : ITenantBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public IServiceCollection Services { get; } = services;

    public void Add(ITenantRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Apply(this);
    }

    public ITenantBuilder<TKey> UseStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore<TKey>
    {
        TenantStores.ThrowIfRegistered<TKey>(Services);
        Services.AddScoped<ITenantStore<TKey>, TStore>();
        return this;
    }
}

/// <summary>
/// Checks that an application registers one tenant store.
/// </summary>
internal static class TenantStores
{
    public static void ThrowIfRegistered<TKey>(IServiceCollection services)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ITenantStore<TKey>) && !descriptor.IsKeyedService))
        {
            throw new InvalidOperationException(
                $"A tenant store is already registered for ITenantStore<{typeof(TKey).Name}>. Register one store, with " +
                "UseStore or UseInMemoryStore.");
        }
    }
}
