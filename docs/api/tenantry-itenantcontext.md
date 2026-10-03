# `ITenantContext<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The current tenant, for a request or an [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) scope. A singleton over an `AsyncLocal<T>`: the value belongs to the async flow, not to the instance.

```csharp
public interface ITenantContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Properties

### `CurrentTenant`

The current tenant, or `null` when none is current: no tenant was resolved for the request, or the code runs outside a tenant scope.

```csharp
ITenantDescriptor<TKey>? CurrentTenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md)

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

## Methods

### `GetCurrentTenant<TTenant>()`

The current tenant as `TTenant`, the type your tenant store returns, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) if no tenant is current. Equivalent to `CurrentTenant?.As<TTenant>()`.

```csharp
TTenant? GetCurrentTenant<TTenant>() where TTenant : class, ITenantDescriptor<TKey>
```

Type parameters:

- `TTenant`: Your tenant type.

Returns: `TTenant`

Exceptions:

- `InvalidOperationException`: The current tenant is not a `TTenant`: the tenant store returns another type.

A default interface method: a mock of [`ITenantContext<TKey>`](tenantry-itenantcontext.md) (NSubstitute, Moq) intercepts it and returns [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) unless it is configured too, so code under test with a mocked context can read `CurrentTenant?.As<TTenant>()` instead, or tests can use a real context ([`ITenantContextSetter<TKey>.Use`](tenantry-itenantcontextsetter.md)).

```csharp
app.MapGet("/plan", (ITenantContext<Guid> tenants) => tenants.GetCurrentTenant<AppTenant>()?.Plan);
```
