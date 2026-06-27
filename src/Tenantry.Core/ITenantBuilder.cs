using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core;

/// <summary>
/// Minimal builder interface that captures <typeparamref name="TKey"/> and exposes
/// the service collection. Satellite packages (e.g. Tenantry.EfCore) add extension
/// methods on this interface so users only specify TKey once in <c>AddTenantry</c>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
public interface ITenantBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Gets the underlying service collection.</summary>
    IServiceCollection Services { get; }
    
    /// <summary>
    /// Registers a pre-populated in-memory tenant store.
    /// </summary>
    ITenantBuilder<TKey> UseInMemoryStore(IEnumerable<ITenantDescriptor<TKey>> tenants);

    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation.
    /// </summary>
    /// <remarks>
    /// The store is registered with a <strong>scoped</strong> lifetime and is resolved per operation —
    /// Tenantry creates a fresh scope for singleton/background callers — so the implementation may safely
    /// depend on scoped services such as a <c>DbContext</c>.
    /// </remarks>
    ITenantBuilder<TKey> UseStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore<TKey>;

    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation with a factory function.
    /// </summary>
    /// <remarks>
    /// The store is registered with a <strong>scoped</strong> lifetime and is resolved per operation, so
    /// the factory may return an instance that depends on scoped services such as a <c>DbContext</c>.
    /// </remarks>
    ITenantBuilder<TKey> UseStore(Func<IServiceProvider, ITenantStore<TKey>> factory);
}
