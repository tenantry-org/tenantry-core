# `TenantryEfCoreTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for configuring EF Core tenant isolation on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md).

```csharp
public static class TenantryEfCoreTenantBuilderExtensions
```

## Methods

### `AddEfCoreIsolation<TKey>(ITenantBuilder<TKey>, Action<EfCoreIsolationOptions>?)`

Registers EF Core tenant isolation services (the SaveChanges interceptor and the configured isolation policy). Call this inside your `AddTenantry` configuration lambda.

```csharp
public static ITenantBuilder<TKey> AddEfCoreIsolation<TKey>(this ITenantBuilder<TKey> builder, Action<EfCoreIsolationOptions>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<EfCoreIsolationOptions>`: Sets the isolation options, such as what happens to a write without a tenant, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the defaults.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Calling it again configures the same options instance.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore(tenants)
    .AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));
```
