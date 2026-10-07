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
    /// The store is scoped and read through <see cref="ITenantLookup{TKey}"/>, which resolves it from a new scope for
    /// each lookup, so it may depend on scoped services such as a <c>DbContext</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A tenant store is already registered.</exception>
    ITenantBuilder<TKey> UseStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore<TKey>;

    /// <summary>
    /// Adds an activity validator of type <typeparamref name="TValidator"/>, for a check that needs services. A tenant
    /// must pass every validator. Adding the same type again does nothing.
    /// </summary>
    /// <typeparam name="TValidator">The validator type, created through dependency injection.</typeparam>
    /// <remarks>
    /// The validator is a singleton, as <see cref="ITenantActivity{TKey}"/> is, so it must not depend on scoped
    /// services: read the tenant's status from the descriptor the store returns, or create a scope inside the
    /// validator. <see cref="ITenantActivity{TKey}"/> throws <see cref="InvalidOperationException"/> when first
    /// resolved if an <see cref="ITenantActivityValidator{TKey}"/> is registered as scoped or transient.
    /// </remarks>
    /// <returns>The same builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .ValidateTenantActivity&lt;SubscriptionActivityValidator&gt;());
    /// </code>
    /// </example>
    ITenantBuilder<TKey> ValidateTenantActivity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>()
        where TValidator : class, ITenantActivityValidator<TKey>;
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
