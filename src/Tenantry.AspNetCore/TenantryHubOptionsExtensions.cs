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
    /// tenant stops the next calls on the connections it already has open.
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
    /// The check runs once, as a call starts. A streaming method is checked when the client starts the stream, and a
    /// stream, or any other call, that is already running when the tenant is suspended or deleted runs on until it
    /// ends or the client stops it. Messages the server sends with <c>IHubContext</c> are not checked.
    /// </para>
    /// <para>
    /// In an app with Blazor Server, also check circuits with <c>AddInteractiveServerComponents().AddTenantry()</c>.
    /// This filter does not check Blazor Server's own hub.
    /// </para>
    /// </remarks>
    public static void AddTenantry(this HubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddFilter(TenantActivityHubFilter.Instance);
    }
}
