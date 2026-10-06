using Tenantry;
using Tenantry.AspNetCore.Internal;

namespace Microsoft.AspNetCore.SignalR;

/// <summary>
/// Tenantry's check of each SignalR hub method call.
/// </summary>
public static class TenantryHubOptionsExtensions
{
    /// <summary>
    /// Refuses each hub method call whose tenant no longer exists or is no longer active, so suspending or deleting a
    /// tenant stops the calls on the connections it already has open.
    /// </summary>
    /// <param name="options">
    /// The options of every hub (<c>AddSignalR(options =&gt; …)</c>), or of one hub (<c>AddHubOptions&lt;THub&gt;</c>).
    /// </param>
    /// <remarks>
    /// <para>
    /// A connection's tenant is the one <c>app.UseTenantry()</c> resolved for the request that opened it, and it stays
    /// current for every call on the connection. Before each call, the filter looks the tenant up again with
    /// <see cref="ITenantLookup{TKey}"/>, through the cache with <c>CacheTenants</c>, and checks it with
    /// <see cref="ITenantActivity{TKey}"/>. A tenant the store no longer has fails the call with
    /// <see cref="TenantNotFoundException"/>, and one an activity validator refuses with
    /// <see cref="TenantInactiveException"/>. The method does not run, the client sees the call fail, and the
    /// connection stays open. A call on a connection without a tenant is not checked.
    /// </para>
    /// <para>
    /// The connection's tenant itself does not change: the call runs with the tenant as the store returned it when the
    /// connection opened. A call fails with <see cref="InvalidOperationException"/> if Tenantry is not registered.
    /// </para>
    /// <para>
    /// In an app with Blazor Server, add the filter to each of your own hubs with <c>AddHubOptions&lt;THub&gt;</c>, and
    /// check circuits with <c>AddInteractiveServerComponents().AddTenantry()</c>. The options of every hub also reach
    /// Blazor's own hub, where a refused call leaves the circuit open but not responding.
    /// </para>
    /// </remarks>
    public static void AddTenantry(this HubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddFilter(TenantActivityHubFilter.Instance);
    }
}
