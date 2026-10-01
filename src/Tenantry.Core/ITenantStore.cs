using Tenantry.Internal;

namespace Tenantry;

/// <summary>
/// Persists and retrieves tenant definitions.
/// Implement this interface to back tenants with a database, configuration file,
/// or any other store.
/// </summary>
/// <remarks>
/// <para>
/// A custom store registered via <c>UseStore&lt;TStore&gt;()</c> is <strong>scoped</strong>, and Tenantry
/// resolves it per operation from a fresh dependency-injection scope (so singleton/background services
/// can read tenants without capturing it). Implementations may therefore depend on scoped services such
/// as a <c>DbContext</c>, but must not assume a singleton lifetime or cache scope-bound state across calls.
/// </para>
/// <para>
/// Return every tenant that exists, suspended or inactive ones included, from both methods: tools that
/// maintain each tenant's database, such as migrations, find tenants here. Decide whether a tenant may be
/// served elsewhere: with an access validator for HTTP requests, and in your own code for background work.
/// </para>
/// </remarks>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
public interface ITenantStore<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns the tenant with the given <paramref name="tenantId"/>,
    /// or <c>null</c> if no matching tenant exists.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to find.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every tenant that exists, suspended or inactive ones included.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the tenant a request's identifier names, or <c>null</c> if it names none. An identifier is what a
    /// resolver reads from a request: the tenant's id, or a name your store maps to a tenant, such as a subdomain
    /// (<c>acme</c>), a slug in a route or a custom domain (<c>app.acme.com</c>).
    /// </summary>
    /// <param name="identifier">The identifier, as the resolver returned it.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <remarks>
    /// By default the identifier is the tenant's id: it is parsed as <typeparamref name="TKey"/> with the invariant
    /// culture and looked up with <see cref="GetTenantAsync"/>, and an identifier that does not parse, or parses to
    /// the key type's default (<see cref="Guid.Empty"/>, <c>0</c>) or an empty string, names no tenant. Implement it
    /// to resolve tenants by another name, for example a <see cref="Guid"/>-keyed store whose tenants have slugs. A
    /// store that wraps another (to log, say) must forward it to the inner store: otherwise it gets this default,
    /// which never reaches the inner store's own mapping.
    /// </remarks>
    /// <example>
    /// <code>
    /// public async ValueTask&lt;ITenantDescriptor&lt;Guid&gt;?&gt; FindByIdentifierAsync(string identifier, CancellationToken ct) =&gt;
    ///     await db.Tenants.SingleOrDefaultAsync(t =&gt; t.Slug == identifier, ct);
    /// </code>
    /// </example>
    ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
        TenantIds.TryParse<TKey>(identifier, out var tenantId)
            ? GetTenantAsync(tenantId, cancellationToken)
            : ValueTask.FromResult<ITenantDescriptor<TKey>?>(null);
}
