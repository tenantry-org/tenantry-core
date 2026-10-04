# `ITenantContextSetter<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Makes a tenant current for the calling code, for code that has already found the tenant and needs no new dependency-injection scope. Most code uses [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) instead, which also creates a scope for the tenant's services.

Registered as a singleton by `AddTenantry`. The current tenant is ambient (held in an `AsyncLocal<T>`): it flows into code the caller awaits or starts, never back to the caller's caller.

```csharp
public interface ITenantContextSetter<TKey> : ITenantContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `Use(ITenantDescriptor<TKey>)`

Makes `tenant` the current tenant until the returned handle is disposed. Uses may nest: an inner one shadows the outer tenant, and disposing it restores the outer tenant (the outermost one restores "no tenant").

```csharp
IDisposable Use(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant to make current.

Returns: `IDisposable`: A handle that restores the previously current tenant on disposal.

Exceptions:

- `ArgumentException`: The tenant's id is the key type's default value (`Empty`, `0`) or an empty string, which Tenantry reserves for "no tenant".

The tenant is not looked up in the store and not checked with [`ITenantActivity<TKey>`](tenantry-itenantactivity.md): the descriptor passed becomes current as it is, even when the store does not hold its id, the tenant is inactive, or its other fields differ from the store's. Shared-database queries are then filtered by its id and saves stamp new rows with it, so a descriptor the store does not hold leaves rows owned by an id the store does not know.

Pass a tenant you already hold: one that request resolution found, one read from [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), or one being onboarded before its store row exists. For an id from outside the application, such as a queue message or a command-line argument, use [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md), which looks the tenant up and refuses a missing or inactive one.

### `UseNoTenant()`

Makes no tenant current until the returned handle is disposed, as [`ITenantContextSetter<TKey>.Use`](tenantry-itenantcontextsetter.md) makes one current: code inside sees no tenant, and disposing it restores the tenant that was current before.

```csharp
IDisposable UseNoTenant()
```

Returns: `IDisposable`: A handle that restores the previously current tenant on disposal.

For code that must run as no tenant inside a tenant's flow, such as the rest of a request whose tenant was refused after it was made current.
