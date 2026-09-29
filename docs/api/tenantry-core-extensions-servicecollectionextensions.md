# `ServiceCollectionExtensions` class

Namespace: `Tenantry.Core.Extensions` · Package: `Tenantry.Core` · [API reference](README.md)

Extension methods for registering core Tenantry services.

```csharp
public static class ServiceCollectionExtensions
```

## Methods

### `AddTenantryCore<TKey>(IServiceCollection, Action<ITenantBuilder<TKey>>?)`

Registers the core Tenantry services: the tenant context accessor (AsyncLocal singleton) behind [`ITenantContext<TKey>`](tenantry-core-itenantcontext.md) and [`ITenantScope<TKey>`](tenantry-core-itenantscope.md), plus the [`ITenantScopeFactory<TKey>`](tenantry-core-itenantscopefactory.md) and [`ITenantStoreAccessor<TKey>`](tenantry-core-itenantstoreaccessor.md) singletons used by background work.

```csharp
public static IServiceCollection AddTenantryCore<TKey>(this IServiceCollection services, Action<ITenantBuilder<TKey>>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `services` `IServiceCollection`: The application's service collection.
- `configure` `Action<ITenantBuilder<TKey>>`: Configures the tenant store and other Core services, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) to register only the defaults.

Returns: `IServiceCollection`

Use this entry point for worker services, console apps, and other non-HTTP hosts. For ASP.NET Core applications, use `AddTenantry<TKey>()` instead, which calls this method internally and adds HTTP-specific resolution on top.

All registrations are idempotent — calling both `AddTenantryCore` and `AddTenantry` is safe.

```csharp
// Worker service — no ASP.NET Core required
builder.Services.AddTenantryCore<Guid>(tenant =>
{
    tenant.AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject);
});

// Run EF Core work as a tenant, in its own DI scope: var scopes = sp.GetRequiredService<ITenantScopeFactory<Guid>>(); await using (var scope = scopes.CreateScope(tenant)) {     var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();     // reads are filtered to the tenant and writes are stamped with it } ```
