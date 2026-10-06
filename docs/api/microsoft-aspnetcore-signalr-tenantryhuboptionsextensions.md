# `TenantryHubOptionsExtensions` class

Namespace: `Microsoft.AspNetCore.SignalR` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Tenantry's check of each SignalR hub method call.

```csharp
public static class TenantryHubOptionsExtensions
```

## Methods

### `AddTenantry(HubOptions)`

Refuses each hub method call whose tenant no longer exists or is no longer active, so suspending or deleting a tenant stops the calls on the connections it already has open.

```csharp
public static void AddTenantry(this HubOptions options)
```

Parameters:

- `options` `HubOptions`: The options of every hub (`AddSignalR(options => …)`), or of one hub (`AddHubOptions<THub>`).

A connection's tenant is the one `app.UseTenantry()` resolved for the request that opened it, and it stays current for every call on the connection. Before each call, the filter looks the tenant up again with [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), through the cache with `CacheTenants`, and checks it with [`ITenantActivity<TKey>`](tenantry-itenantactivity.md). A tenant the store no longer has fails the call with [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md), and one an activity validator refuses with [`TenantInactiveException`](tenantry-tenantinactiveexception.md). The method does not run, the client sees the call fail, and the connection stays open. A call on a connection without a tenant is not checked.

The connection's tenant itself does not change: the call runs with the tenant as the store returned it when the connection opened. A call fails with `InvalidOperationException` if Tenantry is not registered.

In an app with Blazor Server, add the filter to each of your own hubs with `AddHubOptions<THub>`, and check circuits with `AddInteractiveServerComponents().AddTenantry()`. The options of every hub also reach Blazor's own hub, where a refused call leaves the circuit open but not responding.
