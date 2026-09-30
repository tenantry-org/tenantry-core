namespace Tenantry;

/// <summary>
/// How to find each tenant's connection string, for applications that give tenants their own database (or
/// route them to different servers). Configure it with <c>UseConnectionStrings</c> and read connection strings
/// through <see cref="ITenantConnectionStringProvider{TKey}"/>, or the current tenant's through
/// <see cref="CurrentTenantConnectionString{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Set at least one delegate. <see cref="ITenantConnectionStringProvider{TKey}.GetAsync"/>
/// prefers <see cref="GetConnectionStringAsync"/> and falls back to <see cref="GetConnectionString"/>;
/// the synchronous <c>Get</c> needs <see cref="GetConnectionString"/>. The default provider does not cache.
/// </remarks>
public sealed class TenantConnectionStringOptions<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns the connection string for a tenant. It runs whenever a connection string is read (for
    /// example each time a <c>DbContext</c> is created), so it should be quick: build the string from the
    /// tenant's properties rather than calling another service.
    /// </summary>
    /// <example>
    /// <code>
    /// options.GetConnectionString = tenant =&gt; $"Server=db;Database=app_{tenant.TenantId};Integrated Security=true";
    /// </code>
    /// </example>
    public Func<ITenantDescriptor<TKey>, string>? GetConnectionString { get; set; }

    /// <summary>
    /// Returns the connection string for a tenant asynchronously, for connection strings held elsewhere such
    /// as a secrets vault. Only the asynchronous <c>GetAsync</c> methods can use it.
    /// </summary>
    public Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<string>>? GetConnectionStringAsync { get; set; }
}
