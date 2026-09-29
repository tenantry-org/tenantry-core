# `ServiceCollectionExtensions` class

Namespace: `Tenantry.AspNetCore.Extensions` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

Extension methods for registering Tenantry services.

```csharp
public static class ServiceCollectionExtensions
```

## Methods

### `AddTenantry<TKey>(IServiceCollection, Action<IAspNetCoreTenantBuilder<TKey>>)`

Registers Tenantry services for ASP.NET Core and returns an [`IAspNetCoreTenantBuilder<TKey>`](tenantry-aspnetcore-iaspnetcoretenantbuilder.md) for further configuration.

```csharp
public static IServiceCollection AddTenantry<TKey>(this IServiceCollection services, Action<IAspNetCoreTenantBuilder<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type (e.g. `Guid`, `int`, `string`). Must implement `IEquatable<T>` and `IParsable<TSelf>`.

Parameters:

- `services` `IServiceCollection`: The application's service collection.
- `configure` `Action<IAspNetCoreTenantBuilder<TKey>>`: Configures tenant resolution, the tenant store and access validation.

Returns: `IServiceCollection`

```csharp
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore(tenants);
    tenant.AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject);
});
```
