# `TenantryServiceCollectionExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Core` · [API reference](README.md)

Registers Tenantry.

```csharp
public static class TenantryServiceCollectionExtensions
```

## Methods

### `AddTenantry<TKey>(IServiceCollection, Action<ITenantBuilder<TKey>>?)`

Registers Tenantry for tenant keys of type `TKey`, then calls `configure` to add its features: a tenant store, how requests are resolved to tenants, connection strings, EF Core isolation and so on.

```csharp
public static IServiceCollection AddTenantry<TKey>(this IServiceCollection services, Action<ITenantBuilder<TKey>>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type (e.g. `Guid`, `int`, `string`). Must implement `IEquatable<T>` and `IParsable<TSelf>`.

Parameters:

- `services` `IServiceCollection`: The application's service collection.
- `configure` `Action<ITenantBuilder<TKey>>`: Adds Tenantry's features, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) to register only the core services.

Returns: `IServiceCollection`: The same `services` for chaining.

Exceptions:

- `InvalidOperationException`: Tenantry is already registered with another tenant key type.

The core services are the ambient tenant ([`ITenantContext<TKey>`](tenantry-itenantcontext.md) and [`ITenantContextSetter<TKey>`](tenantry-itenantcontextsetter.md)), [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) and [`ITenantStoreAccessor<TKey>`](tenantry-itenantstoreaccessor.md), all singletons. They serve web applications, workers and console tools alike; the ASP.NET Core features come from the Tenantry.AspNetCore package.

Calling it again adds to the same registration, so a library can call it to make sure Tenantry is registered. An application uses one tenant key type: calling it with another throws.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .AddEfCoreIsolation());
```
