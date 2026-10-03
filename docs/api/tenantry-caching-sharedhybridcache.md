# `SharedHybridCache` class

Namespace: `Tenantry.Caching` · Package: `Tenantry.Caching` · [API reference](README.md)

The `HybridCache` for entries every tenant shares (exchange rates, reference data), where `IsolateCaches()` makes the injected `HybridCache` keep entries per tenant. Inject it where sharing is meant, so the constructor says so. Its keys and tags are its own: they never name a tenant's entry, and a tenant's never name one of these.

It is the `HybridCache` registered before `IsolateCaches()`, with its serializers and options, and works with or without a current tenant. Microsoft's HybridCache runs a factory without the caller's async context, so a factory here runs with no current tenant: load data that no tenant owns.

```csharp
public sealed class ExchangeRates(SharedHybridCache cache)
{
    public ValueTask<decimal> GetAsync(string currency, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{currency}", async token => await LoadRateAsync(currency, token), cancellationToken: ct);
}
```

```csharp
public sealed class SharedHybridCache : HybridCache, IDisposable
```

Inherits `HybridCache`.

Implements `IDisposable`.

## Methods

### `Dispose()`

Disposes the cache it wraps, when that cache was created for it and is disposable.

```csharp
public void Dispose()
```

### `GetOrCreateAsync<TState, T>(string, TState, Func<TState, CancellationToken, ValueTask<T>>, HybridCacheEntryOptions?, IEnumerable<string>?, CancellationToken)`

Asynchronously gets the value associated with the key if it exists, or generates a new entry using the provided key and a value from the given factory if the key is not found.

```csharp
public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
```

Type parameters:

- `TState`: The type of additional state required by `factory`.
- `T`: The type of the data being considered.

Parameters:

- `key` `string`: The key of the entry to look for or create.
- `state` `TState`: The state required for `factory`.
- `factory` `Func<TState, CancellationToken, ValueTask<T>>`: Provides the underlying data service if the data is not available in the cache.
- `options` `HybridCacheEntryOptions`: Additional options for this cache entry.
- `tags` `IEnumerable<string>`: The tags to associate with this cache item.
- `cancellationToken` `CancellationToken`: The `CancellationToken` used to propagate notifications that the operation should be canceled.

Returns: `ValueTask<T>`: The data, either from cache or the underlying data service.

### `RemoveAsync(IEnumerable<string>, CancellationToken)`

Removes the shared entries `keys`.

```csharp
public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
```

Parameters:

- `keys` `IEnumerable<string>`: The entries' keys, as they were written here.
- `cancellationToken` `CancellationToken`: Cancels the removal.

Returns: `ValueTask`: A task that completes when the entries are removed.

### `RemoveAsync(string, CancellationToken)`

Removes the shared entry `key`.

```csharp
public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
```

Parameters:

- `key` `string`: The entry's key, as it was written here.
- `cancellationToken` `CancellationToken`: Cancels the removal.

Returns: `ValueTask`: A task that completes when the entry is removed.

### `RemoveByTagAsync(IEnumerable<string>, CancellationToken)`

Removes the shared entries tagged with any of `tags`.

```csharp
public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
```

Parameters:

- `tags` `IEnumerable<string>`: The tags, as entries were written with them here.
- `cancellationToken` `CancellationToken`: Cancels the removal.

Returns: `ValueTask`: A task that completes when the entries are removed.

### `RemoveByTagAsync(string, CancellationToken)`

Removes the shared entries tagged `tag`.

```csharp
public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
```

Parameters:

- `tag` `string`: The tag, as entries were written with it here.
- `cancellationToken` `CancellationToken`: Cancels the removal.

Returns: `ValueTask`: A task that completes when the entries are removed.

### `SetAsync<T>(string, T, HybridCacheEntryOptions?, IEnumerable<string>?, CancellationToken)`

Asynchronously sets or overwrites the value associated with the key.

```csharp
public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
```

Type parameters:

- `T`: The type of the data being considered.

Parameters:

- `key` `string`: The key of the entry to create.
- `value` `T`: The value to assign for this cache entry.
- `options` `HybridCacheEntryOptions`: Additional options for this cache entry.
- `tags` `IEnumerable<string>`: The tags to associate with this cache entry.
- `cancellationToken` `CancellationToken`: The `CancellationToken` used to propagate notifications that the operation should be canceled.

Returns: `ValueTask`
