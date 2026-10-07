# `ITenantBuilder<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The builder `AddTenantry<TKey>` passes to its configuration callback. Tenantry's features are extension methods on it that return it, so calls chain.

```csharp
public interface ITenantBuilder<TKey> : ITenantBuilder where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must implement `IEquatable<T>` and `IParsable<TSelf>`.

## Methods

### `UseStore<TStore>()`

Registers a custom [`ITenantStore<TKey>`](tenantry-itenantstore.md) implementation.

```csharp
ITenantBuilder<TKey> UseStore<TStore>() where TStore : class, ITenantStore<TKey>
```

Type parameters:

- `TStore`: The store type, created through dependency injection.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md)

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.

The store is scoped and read through [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), which resolves it from a new scope for each lookup, so it may depend on scoped services such as a `DbContext`.

### `ValidateTenantActivity<TValidator>()`

Adds an activity validator of type `TValidator`, for a check that needs services. A tenant must pass every validator. Adding the same type again does nothing.

```csharp
ITenantBuilder<TKey> ValidateTenantActivity<TValidator>() where TValidator : class, ITenantActivityValidator<TKey>
```

Type parameters:

- `TValidator`: The validator type, created through dependency injection.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same builder for chaining.

The validator is a singleton, as [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) is, so it must not depend on scoped services: read the tenant's status from the descriptor the store returns, or create a scope inside the validator. [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) throws `InvalidOperationException` when first resolved if an [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) is registered as scoped or transient.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .ValidateTenantActivity<SubscriptionActivityValidator>());
```
