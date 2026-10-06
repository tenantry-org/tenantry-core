# `TenantryTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Core` · [API reference](README.md)

Tenantry's core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): the tenant store, its cache, invalidation across instances, and per-tenant connection strings.

```csharp
public static class TenantryTenantBuilderExtensions
```

## Methods

### `BroadcastInvalidations<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantInvalidationHandler<TKey>>)`

Adds a handler that publishes each invalidation to the application's other instances, through a message bus or a pub/sub channel of your own.

```csharp
public static ITenantBuilder<TKey> BroadcastInvalidations<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantInvalidationHandler<TKey>> factory) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `factory` `Func<IServiceProvider, ITenantInvalidationHandler<TKey>>`: Creates the handler, once, from the application's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

The handler is a singleton. [`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md) and [`ITenantInvalidator<TKey>.InvalidateAllAsync`](tenantry-itenantinvalidator.md) run it after every other handler, and [`ITenantInvalidator<TKey>.InvalidateLocallyAsync`](tenantry-itenantinvalidator.md) and [`ITenantInvalidator<TKey>.InvalidateAllLocallyAsync`](tenantry-itenantinvalidator.md) do not run it. Each instance applies an invalidation it receives with the local methods, so it does not publish it again.

The handler is called after this instance is invalidated. When it throws, the invalidator throws its exception once every handler has run: this instance is invalidated, and the others keep their copies until those expire or a retry reaches them.

### `CacheTenants<TKey>(ITenantBuilder<TKey>, Action<TenantStoreCacheOptions>?)`

Caches the tenants that Tenantry's own lookups (`app.UseTenantry()` and [`ITenantLookup<TKey>`](tenantry-itenantlookup.md)) find in the store, for [`TenantStoreCacheOptions.Duration`](tenantry-tenantstorecacheoptions.md) (5 minutes by default).

```csharp
public static ITenantBuilder<TKey> CacheTenants<TKey>(this ITenantBuilder<TKey> builder, Action<TenantStoreCacheOptions>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantStoreCacheOptions>`: Sets how long a tenant is cached, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the default.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `ArgumentOutOfRangeException`: [`TenantStoreCacheOptions.Duration`](tenantry-tenantstorecacheoptions.md) is not positive.

Lookups that find nothing, and [`ITenantStore<TKey>.GetAllTenantsAsync`](tenantry-itenantstore.md), are not cached. Call [`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md) when a tenant changes. It uses a registered `TimeProvider` if there is one.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain()
    .UseStore<AppTenantStore>()
    .CacheTenants(o => o.Duration = TimeSpan.FromMinutes(1)));
```

### `DecorateConnectionStrings<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantConnectionStringProvider<TKey>, ITenantConnectionStringProvider<TKey>>)`

Wraps the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md), for example to cache or log. The decorator applies whether this is called before or after `UseConnectionStrings`; several decorators wrap in the order they are added, so the last one added is called first.

```csharp
public static ITenantBuilder<TKey> DecorateConnectionStrings<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantConnectionStringProvider<TKey>, ITenantConnectionStringProvider<TKey>> decorate) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `decorate` `Func<IServiceProvider, ITenantConnectionStringProvider<TKey>, ITenantConnectionStringProvider<TKey>>`: Returns the provider to use in place of the one it is given. It runs once, when the provider is first resolved.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

A decorator should forward [`ITenantConnectionStringProvider<TKey>.CanGetSynchronously`](tenantry-itenantconnectionstringprovider.md) to the provider it wraps. Resolving the provider without any connection strings configured throws `InvalidOperationException`.

### `IgnoreWarnings<TKey>(ITenantBuilder<TKey>, params int[])`

Stops Tenantry logging the warnings `eventIds`, each about configuration the application may have chosen on purpose. [`TenantryWarnings`](tenantry-tenantrywarnings.md) names the ones it accepts.

```csharp
public static ITenantBuilder<TKey> IgnoreWarnings<TKey>(this ITenantBuilder<TKey> builder, params int[] eventIds) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `eventIds` `int[]`: The warnings' event ids.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `ArgumentException`: An id is not one that [`TenantryWarnings`](tenantry-tenantrywarnings.md) names.

Each call adds to the ids of the others.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<AppTenantStore>()
    .IgnoreWarnings(TenantryWarnings.StringTenantIdCollation));
```

### `UseConnectionStrings<TKey>(ITenantBuilder<TKey>, Action<TenantConnectionStringOptions<TKey>>)`

Configures how each tenant's connection string is found.

```csharp
public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(this ITenantBuilder<TKey> builder, Action<TenantConnectionStringOptions<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantConnectionStringOptions<TKey>>`: Sets the delegates that return a tenant's connection string.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: Neither delegate is set after `configure` runs, or another provider is already registered.

Registers [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) and [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md) as singletons. Calling it again configures the same options instance, so a later call can replace a delegate. It cannot be combined with `UseConnectionStrings(sp => …)` or an [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) the application registered before `AddTenantry`.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true"));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<CurrentTenantConnectionString<string>>().Get()));
```

### `UseConnectionStrings<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantConnectionStringProvider<TKey>>)`

Registers the provider that returns each tenant's connection string, built from the application's services, so it can use a secrets client or other services registered in DI.

```csharp
public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantConnectionStringProvider<TKey>> factory) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `factory` `Func<IServiceProvider, ITenantConnectionStringProvider<TKey>>`: Creates the provider, once, from the application's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: `UseConnectionStrings(options => …)` was called before it.

Registers [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) and [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md) as singletons. It replaces a provider set before by this method or registered by the application before `AddTenantry`, and cannot be combined with `UseConnectionStrings(options => …)`. A provider that can only read connection strings asynchronously returns [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) from [`ITenantConnectionStringProvider<TKey>.CanGetSynchronously`](tenantry-itenantconnectionstringprovider.md).

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(sp => new VaultConnectionStrings(sp.GetRequiredService<SecretClient>())));
```

### `UseInMemoryStore<TKey>(ITenantBuilder<TKey>, IEnumerable<ITenantDescriptor<TKey>>)`

Registers an in-memory tenant store that holds `tenants`, for tests and single-instance deployments whose tenants do not change.

```csharp
public static ITenantBuilder<TKey> UseInMemoryStore<TKey>(this ITenantBuilder<TKey> builder, IEnumerable<ITenantDescriptor<TKey>> tenants) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `tenants` `IEnumerable<ITenantDescriptor<TKey>>`: The tenants the store holds. The store does not change after registration.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.
- `ArgumentException`: A tenant has an id Tenantry reserves for "no tenant" ([`TenantIds.IsReserved<TKey>`](tenantry-tenantids.md)), two tenants have the same id, or, with `string` ids, two ids differ only in case.

The store is a singleton, built when this is called.

### `UseStore<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantStore<TKey>>)`

Registers a custom [`ITenantStore<TKey>`](tenantry-itenantstore.md) implementation with a factory function.

```csharp
public static ITenantBuilder<TKey> UseStore<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantStore<TKey>> factory) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `factory` `Func<IServiceProvider, ITenantStore<TKey>>`: Creates the store from the scope's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.

The store is scoped and read through [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), which resolves it from a new scope for each lookup, so the factory may use scoped services such as a `DbContext`.

### `ValidateTenantActivity<TValidator>(ITenantBuilder)`

Adds an activity validator of type `TValidator`, for a check that needs services. A tenant must pass every validator.

```csharp
public static ITenantBuilder ValidateTenantActivity<TValidator>(this ITenantBuilder builder) where TValidator : class
```

Type parameters:

- `TValidator`: The validator type, which implements [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) for the application's tenant key type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same `builder`, without its key type: call methods that need it first, or call it as a statement of its own.

Exceptions:

- `InvalidOperationException`: `TValidator` does not implement [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) for the builder's key type.

The validator is a singleton, as [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) is, so it must not depend on scoped services: read the tenant's status from the descriptor the store returns, or create a scope inside the validator. [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) throws `InvalidOperationException` when first resolved if an [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) is registered as scoped or transient.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ValidateTenantActivity<SubscriptionActivityValidator>());
```

### `ValidateTenantActivity<TKey>(ITenantBuilder<TKey>, Func<ITenantDescriptor<TKey>, bool>)`

Stops work for tenants that `isActive` refuses, such as suspended ones: requests (with Tenantry.AspNetCore), `RunInScopeAsync`, and Tenantry.Pro's background work, jobs and messages.

```csharp
public static ITenantBuilder<TKey> ValidateTenantActivity<TKey>(this ITenantBuilder<TKey> builder, Func<ITenantDescriptor<TKey>, bool> isActive) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `isActive` `Func<ITenantDescriptor<TKey>, bool>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when work may run for the tenant.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Calling it again adds another check: a tenant must pass all of them. See [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) for where Tenantry checks.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ValidateTenantActivity(t => t.As<AppTenant>().IsActive));
```

### `ValidateTenantActivity<TKey>(ITenantBuilder<TKey>, Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>)`

Stops work for tenants that `isActive` refuses, such as suspended ones: requests (with Tenantry.AspNetCore), `RunInScopeAsync`, and Tenantry.Pro's background work, jobs and messages.

```csharp
public static ITenantBuilder<TKey> ValidateTenantActivity<TKey>(this ITenantBuilder<TKey> builder, Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> isActive) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `isActive` `Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>`: Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when work may run for the tenant.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Calling it again adds another check: a tenant must pass all of them. See [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) for where Tenantry checks.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ValidateTenantActivity(t => t.As<AppTenant>().IsActive));
```
