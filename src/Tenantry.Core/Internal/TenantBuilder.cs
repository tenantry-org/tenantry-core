using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Stores;

namespace Tenantry.Core.Internal;

/// <summary>
/// Provides functionality for configuring tenant-related services within the application using
/// generics for tenant key type <typeparamref name="TKey"/>.
/// </summary>
/// <typeparam name="TKey">
/// The type used for representing tenant keys. Must implement <see cref="IEquatable{T}"/> and
/// <see cref="IParsable{T}"/>.
/// </typeparam>
internal class TenantBuilder<TKey>(IServiceCollection services) : ITenantBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Provides access to the underlying <see cref="IServiceCollection"/> instance used for registering
    /// and configuring dependencies related to tenancy.
    /// </summary>
    /// <remarks>
    /// This property is essential for adding services or custom configurations required
    /// during the tenant building process. It allows extension methods and services to
    /// be registered into the dependency injection container.
    /// </remarks>
    public IServiceCollection Services { get; } = services;
    
    /// <summary>
    /// Registers a pre-populated in-memory tenant store.
    /// Suitable for testing and simple single-instance deployments.
    /// </summary>
    public ITenantBuilder<TKey> UseInMemoryStore(IEnumerable<ITenantDescriptor<TKey>> tenants)
    {
        Services.AddSingleton<ITenantStore<TKey>>(_ => new InMemoryTenantStore<TKey>(tenants));
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation.
    /// </summary>
    public ITenantBuilder<TKey> UseStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore<TKey>
    {
        Services.AddScoped<ITenantStore<TKey>, TStore>();
        return this;
    }
    
    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation with a factory function.
    /// </summary>
    public ITenantBuilder<TKey> UseStore(
        Func<IServiceProvider, ITenantStore<TKey>> factory)
    {
        Services.AddScoped(factory);
        return this;
    }
}
