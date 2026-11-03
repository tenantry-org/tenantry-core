# `TenantryServerSideBlazorBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Tenantry's check of each Blazor Server circuit's activity.

```csharp
public static class TenantryServerSideBlazorBuilderExtensions
```

## Methods

### `AddTenantry(IServerSideBlazorBuilder)`

Ends a Blazor Server circuit whose tenant no longer exists or is no longer active, at the circuit's next browser event, JavaScript interop call or navigation, so suspending or deleting a tenant stops the circuits it already has open.

```csharp
public static IServerSideBlazorBuilder AddTenantry(this IServerSideBlazorBuilder builder)
```

Parameters:

- `builder` `IServerSideBlazorBuilder`: The builder `AddInteractiveServerComponents()` or `AddServerSideBlazor()` returns.

Returns: `IServerSideBlazorBuilder`: The same `builder` for chaining.

A circuit's tenant is the one `app.UseTenantry()` resolved for the request that opened its connection. Blazor runs each inbound activity on the circuit's current connection, and when it reconnects a circuit, that is a new connection, whose request may resolve another tenant or none (as it does for a tenant suspended in the meantime). So before each activity, a circuit handler first checks that the connection's tenant is the circuit's. A connection without a tenant throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), and one with another tenant, or with a tenant for a circuit that opened without one, `InvalidOperationException`.

The handler then looks the circuit's tenant up again with [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), through the cache with `CacheTenants`, and checks it with [`ITenantActivity<TKey>`](tenantry-itenantactivity.md). A tenant the store no longer has throws [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md), and one an activity validator refuses [`TenantInactiveException`](tenantry-tenantinactiveexception.md). A circuit that opened without a tenant, and still has none, is not checked.

On .NET 10 and later, Blazor can pause a circuit (`Blazor.pauseCircuit()`, or when it evicts a disconnected one) and resume its state in a new circuit, whose tenant is the resuming request's. The handler records the circuit's tenant in the state Blazor keeps, and refuses the resumed circuit's first activity, which adds its components, when the request that resumed it resolved another tenant or none, or when the state records no tenant (a circuit paused without this handler): [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) for a request without a tenant, and `InvalidOperationException` otherwise. The circuit then ends before it renders anything.

A refused activity does not run, and Blazor ends the circuit as it does for any unhandled exception.

An activity throws `InvalidOperationException` if Tenantry is not registered.
