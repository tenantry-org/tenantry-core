# `ITenantScope<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

A dependency-injection scope with a tenant current, created by [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md).

Disposing the scope (synchronously or asynchronously) disposes its services while the tenant is still current, then restores the tenant that was current when the scope was created. Both forms restore the tenant in the disposing code's own context, so `await using` is safe in loops and nested scopes.

```csharp
public interface ITenantScope<TKey> : IServiceScope, IDisposable, IAsyncDisposable where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor.md) for constraints.

## Properties

### `Tenant`

The tenant this scope runs as.

```csharp
ITenantDescriptor<TKey> Tenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor.md)
