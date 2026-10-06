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

A circuit's tenant is the one `app.UseTenantry()` resolved for the request that opened its connection, and it stays current for the circuit's lifetime. Before each inbound activity, a circuit handler looks the tenant up again with [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), through the cache with `CacheTenants`, and checks it with [`ITenantActivity<TKey>`](tenantry-itenantactivity.md). A tenant the store no longer has throws [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md), and one an activity validator refuses [`TenantInactiveException`](tenantry-tenantinactiveexception.md). The activity does not run, and Blazor ends the circuit as it does for any unhandled exception. A circuit without a tenant is not checked.

An activity throws `InvalidOperationException` if Tenantry is not registered.
