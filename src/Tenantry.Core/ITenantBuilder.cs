using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry;

/// <summary>
/// The builder <c>AddTenantry</c> passes to its configuration callback, without the tenant key type. Features
/// that take a type parameter of their own register through <see cref="Add"/>, so their callers never repeat
/// the key type.
/// </summary>
public interface ITenantBuilder
{
    /// <summary>Gets the application's service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Applies <paramref name="registration"/> with the tenant key type this builder was created for.
    /// </summary>
    /// <param name="registration">The registration to apply.</param>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    void Add(ITenantRegistration registration);
}

/// <summary>
/// The builder <c>AddTenantry&lt;TKey&gt;</c> passes to its configuration callback. Tenantry's features are
/// extension methods on it that return it, so calls chain.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
public interface ITenantBuilder<TKey> : ITenantBuilder
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation.
    /// </summary>
    /// <typeparam name="TStore">The store type, created through dependency injection.</typeparam>
    /// <remarks>
    /// The store is registered with a <strong>scoped</strong> lifetime and is resolved per operation —
    /// Tenantry creates a fresh scope for singleton/background callers — so the implementation may safely
    /// depend on scoped services such as a <c>DbContext</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A tenant store is already registered.</exception>
    ITenantBuilder<TKey> UseStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore<TKey>;
}

/// <summary>
/// A registration that needs the tenant key type, added through <see cref="ITenantBuilder.Add"/>. Packages
/// use it for builder methods that take a type parameter of their own, such as a <c>DbContext</c> type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantRegistration
{
    /// <summary>Registers the feature's services for the tenant key type <typeparamref name="TKey"/>.</summary>
    /// <typeparam name="TKey">The tenant key type of the builder.</typeparam>
    /// <param name="tenant">The builder the registration was added to.</param>
    void Apply<TKey>(ITenantBuilder<TKey> tenant)
        where TKey : IEquatable<TKey>, IParsable<TKey>;
}
