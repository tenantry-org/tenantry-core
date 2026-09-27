using Tenantry.Core.Exceptions;

namespace Tenantry.Core;

/// <summary>
/// The default <see cref="ITenantConnectionStringResolver{TKey}"/>: calls the configured delegates on every
/// resolution, without caching.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Public so that other resolvers (for example a caching one) can wrap it. <c>UseConnectionStrings</c>
/// registers it as a singleton, and forwards <see cref="ITenantConnectionStringResolver{TKey}"/> to it.
/// </remarks>
public sealed class TenantConnectionStringResolver<TKey>(
    ITenantContext<TKey> tenantContext,
    TenantConnectionStringOptions<TKey> options)
    : ITenantConnectionStringResolver<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private const string NotConfigured =
        "No connection string delegate is configured. Set GetConnectionString or GetConnectionStringAsync with " +
        "UseConnectionStrings, for example: options.GetConnectionString = tenant => $\"...Database=app_{tenant.TenantId}\".";

    /// <inheritdoc />
    public string Resolve() => Resolve(CurrentTenant());

    /// <inheritdoc />
    public ValueTask<string> ResolveAsync(CancellationToken cancellationToken = default) =>
        ResolveAsync(CurrentTenant(), cancellationToken);

    /// <inheritdoc />
    public string Resolve(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var getConnectionString = options.GetConnectionString
            ?? throw new InvalidOperationException(options.GetConnectionStringAsync is null
                ? NotConfigured
                : "Only GetConnectionStringAsync is configured, so connection strings can only be resolved " +
                  "asynchronously. Call ResolveAsync, or also set GetConnectionString for synchronous callers such " +
                  "as an AddDbContext options callback.");

        return Checked(getConnectionString(tenant), tenant);
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (options.GetConnectionStringAsync is { } getConnectionStringAsync)
        {
            return Checked(await getConnectionStringAsync(tenant, cancellationToken), tenant);
        }

        var getConnectionString = options.GetConnectionString ?? throw new InvalidOperationException(NotConfigured);

        return Checked(getConnectionString(tenant), tenant);
    }

    private ITenantDescriptor<TKey> CurrentTenant() =>
        tenantContext.CurrentTenant
        ?? throw new TenantNotResolvedException(
            "No tenant is current, so there is no connection string to resolve. Resolve it during a request " +
            "(after app.UseTenantry()) or inside a scope from ITenantScopeFactory, or pass the tenant explicitly.");

    private static string Checked(string? connectionString, ITenantDescriptor<TKey> tenant) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The connection string delegate returned an empty value for tenant '{tenant.TenantId}'.")
            : connectionString;
}
