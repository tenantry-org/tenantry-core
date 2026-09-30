# `ITenantContext<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Provides read-only access to the currently resolved tenant for the active request scope. Registered as a singleton backed by `AsyncLocal<T>` — the value is per-async-context (effectively per HTTP request) rather than per-instance.

```csharp
public interface ITenantContext<out TKey> where TKey : IEquatable<out TKey>, IParsable<out TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor.md) for constraints.

## Properties

### `CurrentTenant`

The currently resolved tenant, or `null` if no tenant has been resolved (e.g. before the middleware has run, or on anonymous endpoints).

```csharp
ITenantDescriptor<out TKey>? CurrentTenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor.md)

### `CurrentTenantId`

The current tenant's identifier, or `default(TKey)` if no tenant is current: [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for reference-type keys such as [string](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types), but `Empty` or `0` for value-type keys, because `TKey?` is not nullable for them. Check [`ITenantContext<TKey>.HasTenant`](tenantry-itenantcontext.md) to tell "no tenant" apart; Tenantry never lets a tenant have the default id. Equivalent to `CurrentTenant?.TenantId`.

```csharp
TKey? CurrentTenantId { get; }
```

Value: `TKey`

### `HasTenant`

Returns `true` if a tenant has been resolved for the current scope.

```csharp
bool HasTenant { get; }
```

Value: `bool`
