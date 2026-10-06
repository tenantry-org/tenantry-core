# `ITenantActivityValidator<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Decides whether a tenant may have work run for it: requests, background work, jobs and messages. Return [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) for a suspended tenant, so suspending it stops all its work.

Register one with `tenant.ValidateTenantActivity<TValidator>()`, which makes it a singleton. Every registered validator must allow a tenant. [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) asks them. It is a singleton, so a validator must not depend on scoped services: read what it needs from the tenant, which the store returns, or create a scope inside the validator. [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) throws `InvalidOperationException` when first resolved if a validator is registered as scoped or transient.

```csharp
public interface ITenantActivityValidator<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Methods

### `IsActiveAsync(ITenantDescriptor<TKey>, CancellationToken)`

Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when work may run for `tenant`.

```csharp
ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant, as the store returned it.
- `cancellationToken` `CancellationToken`: Cancels the check.

Returns: `ValueTask<bool>`
