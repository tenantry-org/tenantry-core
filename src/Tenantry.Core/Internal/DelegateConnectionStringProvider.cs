namespace Tenantry.Internal;

/// <summary>
/// The provider <c>UseConnectionStrings(options =&gt; …)</c> registers: calls the configured delegates on every call,
/// without caching.
/// </summary>
internal sealed class DelegateConnectionStringProvider<TKey>(TenantConnectionStringOptions<TKey> options)
    : ITenantConnectionStringProvider<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private const string NotConfigured =
        "No connection string delegate is configured. Set GetConnectionString or GetConnectionStringAsync with " +
        "UseConnectionStrings, for example: options.GetConnectionString = tenant => $\"...Database=app_{tenant.TenantId}\".";

    public bool CanGetSynchronously => options.GetConnectionString is not null;

    public string Get(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var getConnectionString = options.GetConnectionString
            ?? throw new InvalidOperationException(options.GetConnectionStringAsync is null
                ? NotConfigured
                : "Only GetConnectionStringAsync is configured, so connection strings can only be read " +
                  "asynchronously. Call GetAsync, or also set GetConnectionString for synchronous callers such " +
                  "as an AddDbContext options callback.");

        return Checked(getConnectionString(tenant), tenant);
    }

    public async ValueTask<string> GetAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (options.GetConnectionStringAsync is { } getConnectionStringAsync)
        {
            return Checked(await getConnectionStringAsync(tenant, cancellationToken), tenant);
        }

        var getConnectionString = options.GetConnectionString ?? throw new InvalidOperationException(NotConfigured);

        return Checked(getConnectionString(tenant), tenant);
    }

    private static string Checked(string? connectionString, ITenantDescriptor<TKey> tenant) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The connection string delegate returned an empty value for tenant '{tenant.TenantId}'.")
            : connectionString;
}
