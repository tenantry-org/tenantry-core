# `ITenantStore<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Persists and retrieves tenant definitions. Implement this interface to back tenants with a database, configuration file, or any other store.

A custom store registered via `UseStore<TStore>()` is **scoped**, and Tenantry resolves it per operation from a fresh dependency-injection scope (so singleton/background services can read tenants without capturing it). Implementations may therefore depend on scoped services such as a `DbContext`, but must not assume a singleton lifetime or cache scope-bound state across calls.

Return every tenant that exists, suspended or inactive ones included, from both methods: tools that maintain each tenant's database, such as migrations, find tenants here. Decide whether work may run for a tenant with an [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) (`tenant.ValidateTenantActivity(…)`).

```csharp
public interface ITenantStore<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

Derived types: [`InMemoryTenantStore<TKey>`](tenantry-inmemorytenantstore.md).

## Methods

### `FindByIdentifierAsync(string, CancellationToken)`

Returns the tenant a request's identifier names, or `null` if it names none. An identifier is what a resolver reads from a request: the tenant's id, or a name your store maps to a tenant, such as a subdomain (`acme`), a slug in a route or a custom domain (`app.acme.com`).

```csharp
ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
```

Parameters:

- `identifier` `string`: The identifier, as the resolver returned it.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`

By default the identifier is the tenant's id: it is parsed as `TKey` with the invariant culture and looked up with [`ITenantStore<TKey>.GetTenantAsync`](tenantry-itenantstore.md), and an identifier that does not parse, or parses to the key type's default (`Empty`, `0`) or an empty string, names no tenant. Implement it to resolve tenants by another name, for example a `Guid`-keyed store whose tenants have slugs. A store that wraps another (to log, say) must forward it to the inner store: otherwise it gets this default, which never reaches the inner store's own mapping.

```csharp
public async ValueTask<ITenantDescriptor<Guid>?> FindByIdentifierAsync(string identifier, CancellationToken ct) =>
    await db.Tenants.SingleOrDefaultAsync(t => t.Slug == identifier, ct);
```

### `GetAllTenantsAsync(CancellationToken)`

Returns every tenant that exists, suspended or inactive ones included.

```csharp
ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>>`

### `GetTenantAsync(TKey, CancellationToken)`

Returns the tenant with the given `tenantId`, or `null` if no matching tenant exists.

```csharp
ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The identifier of the tenant to find.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`
