# `ITenantInvalidator<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Clears everything Tenantry keeps for a tenant when the tenant changes: its cached copy (with `CacheTenants`) and what each [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md) keeps, such as Tenantry.Caching's entries, cached responses and Tenantry.Options' values. Call it when a tenant is suspended, renamed or deleted, or its identifiers or settings change.

`AddTenantry` registers it as a singleton. What it clears is in memory in each instance of the application, or in a cache it shares. To clear the other instances' copies too, register a handler that publishes the invalidation to them with `BroadcastInvalidations`, and have each instance apply what it receives with [`ITenantInvalidator<TKey>.InvalidateLocallyAsync`](tenantry-itenantinvalidator.md) or [`ITenantInvalidator<TKey>.InvalidateAllLocallyAsync`](tenantry-itenantinvalidator.md).

```csharp
app.MapPost("/admin/tenants/{id}/suspend", async (Guid id, AppDbContext db, ITenantInvalidator<Guid> tenants, CancellationToken ct) =>
{
    await db.Tenants.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false), ct);
    await tenants.InvalidateAsync(id, ct);
});
```

```csharp
public interface ITenantInvalidator<in TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `InvalidateAllAsync(CancellationToken)`

Clears what Tenantry keeps for every tenant in this instance, then publishes the invalidation to the other instances through the handlers `BroadcastInvalidations` registered.

```csharp
ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when every handler has run.

Exceptions:

- `Exception`: A handler threw (several: `AggregateException`), after every handler ran.

Handlers run, and fail, as for [`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md).

### `InvalidateAllLocallyAsync(CancellationToken)`

Clears what Tenantry keeps for every tenant in this instance only, without publishing it: for an invalidation another instance published.

```csharp
ValueTask InvalidateAllLocallyAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when every handler that is not a broadcasting one has run.

Exceptions:

- `Exception`: A handler threw (several: `AggregateException`), after every handler ran.

### `InvalidateAsync(TKey, CancellationToken)`

Clears what Tenantry keeps for the tenant `tenantId` in this instance, then publishes the invalidation to the other instances through the handlers `BroadcastInvalidations` registered.

```csharp
ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant that changed.
- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when every handler has run.

Exceptions:

- `ArgumentNullException`: `tenantId` is null.
- `ArgumentException`: `tenantId` is one Tenantry reserves for "no tenant": the key type's default (`Guid.Empty`, `0`) or an empty string.
- `Exception`: A handler threw (several: `AggregateException`), after every handler ran.

Handlers run one after another, the broadcasting ones after the others, and each runs even when an earlier one throws: once they have all run, the call throws the exception, or an `AggregateException` of several. A cancelled `cancellationToken` stops the call before the next handler with an `OperationCanceledException`, in place of any exceptions so far. An exception from a broadcasting handler means this instance is invalidated and some others may not be: they keep their copies until those expire, or until a retry of this call reaches them.

### `InvalidateLocallyAsync(TKey, CancellationToken)`

Clears what Tenantry keeps for the tenant `tenantId` in this instance only, without publishing it: for an invalidation another instance published.

```csharp
ValueTask InvalidateLocallyAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant that changed.
- `cancellationToken` `CancellationToken`: Cancels the invalidation.

Returns: `ValueTask`: A task that completes when every handler that is not a broadcasting one has run.

Exceptions:

- `ArgumentNullException`: `tenantId` is null.
- `ArgumentException`: `tenantId` is one Tenantry reserves for "no tenant".
- `Exception`: A handler threw (several: `AggregateException`), after every handler ran.
