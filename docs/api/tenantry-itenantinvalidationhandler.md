# `ITenantInvalidationHandler<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Clears data kept per tenant when the tenant changes, or, registered with `BroadcastInvalidations`, publishes the invalidation to the application's other instances. Every registered handler runs on [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md) invalidation, with or without `CacheTenants`.

Tenantry.Caching, `IsolateOutputCache()` and Tenantry.Options register their own. Register a handler as a singleton, once, with `TryAddEnumerable`. When the application also injects the handler to read what it keeps, forward the handler registration to that one instance:

```csharp
services.AddSingleton<PriceListCache>();
services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<Guid>, PriceListCache>(
    sp => sp.GetRequiredService<PriceListCache>()));
```

The handlers are resolved the first time a tenant is invalidated, so a handler may depend on [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md). They run one after another, and each runs even when another throws; the exception, or an `AggregateException` of several, is thrown once they have all run. A cancelled token stops them before the next handler, with an `OperationCanceledException` in place of those exceptions. A handler registered with `BroadcastInvalidations` runs after the others, and not for [`ITenantInvalidator<TKey>.InvalidateLocallyAsync`](tenantry-itenantinvalidator.md) or [`ITenantInvalidator<TKey>.InvalidateAllLocallyAsync`](tenantry-itenantinvalidator.md), so a received invalidation, applied with those, is not published again.

```csharp
public interface ITenantInvalidationHandler<in TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `InvalidateAllAsync(CancellationToken)`

Clears what is kept for every tenant.

```csharp
ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when every tenant's data is cleared.

### `InvalidateAsync(TKey, CancellationToken)`

Clears what is kept for the tenant `tenantId`.

```csharp
ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant that changed.
- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when the tenant's data is cleared.
