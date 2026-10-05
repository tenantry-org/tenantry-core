# `ITenantScopeFactory<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Opens tenant scopes for work that runs outside an HTTP request: hosted services, queue consumers, scheduled jobs and console tools. Each scope pairs a fresh dependency-injection scope with an active tenant, so scoped services such as a `DbContext` are created for that tenant and isolated to it.

A singleton registered by `AddTenantry`, used in two ways:

- You have the tenant's id (for example from a queue message): `await scopes.RunInScopeAsync(tenantId, async (scope, ct) => { … }, ct);` It looks the tenant up and refuses a missing or inactive one.
- You already hold the tenant (for example while iterating [`ITenantLookup<TKey>.GetAllTenantsAsync`](tenantry-itenantlookup.md)): `await using var scope = scopes.CreateScope(tenant);` It trusts the descriptor and checks nothing.

There is no `CreateScopeAsync(tenantId)`, as a scope opened inside an asynchronous lookup would not be current for the code that awaited it (the tenant is held in an `AsyncLocal<T>`).

```csharp
public interface ITenantScopeFactory<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `CreateScope(ITenantDescriptor<TKey>)`

Creates a dependency-injection scope and makes `tenant` the current tenant for the calling code until the returned scope is disposed. Services resolved from `ServiceProvider` are created in the new scope and see this tenant.

```csharp
ITenantScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant to activate.

Returns: [`ITenantScope<TKey>`](tenantry-itenantscope.md): The scope. Dispose it (`using` or `await using`) to dispose its services and restore the tenant that was current before it was created.

Exceptions:

- `ArgumentException`: The tenant's id is the key type's default value or an empty string, which Tenantry reserves for "no tenant".

The tenant is not looked up in the store or checked with [`ITenantActivity<TKey>`](tenantry-itenantactivity.md): the descriptor becomes current as it is, even when the store does not hold its id, the tenant is inactive, or its other fields differ from the store's, and shared-database queries and saves use its id, so a descriptor the store does not hold leaves rows owned by an id the store does not know. Per-tenant options from Tenantry.Options are the exception: they are built from the store's copy when the store holds the id.

Pass a tenant you already hold: one read from [`ITenantLookup<TKey>`](tenantry-itenantlookup.md) while iterating the store, or one being onboarded before its store row exists. Reaching inactive tenants suits provisioning and migrations; for other work, check [`ITenantActivity<TKey>`](tenantry-itenantactivity.md) first. For an id from outside the application, such as a queue message or a command-line argument, use [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md), which looks the tenant up and refuses a missing or inactive one.

### `RunInScopeAsync(TKey, Func<ITenantScope<TKey>, CancellationToken, Task>, CancellationToken)`

Looks the tenant up with [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), then runs `work` inside a new scope for it (see [`ITenantScopeFactory<TKey>.CreateScope`](tenantry-itenantscopefactory.md)). The scope is disposed when the work completes or throws. The caller's current tenant is never changed.

```csharp
Task RunInScopeAsync(TKey tenantId, Func<ITenantScope<TKey>, CancellationToken, Task> work, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant to run the work as.
- `work` `Func<ITenantScope<TKey>, CancellationToken, Task>`: The work to run. It receives the scope and `cancellationToken`.
- `cancellationToken` `CancellationToken`: Passed to the tenant lookup and to `work`.

Returns: `Task`

Exceptions:

- [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md): The tenant store has no tenant with that id.
- [`TenantInactiveException`](tenantry-tenantinactiveexception.md): An [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) refused the tenant.
- `InvalidOperationException`: No tenant store is registered.
- `ArgumentException`: `tenantId` is the key type's default value or an empty string, which no tenant can have.
- `OperationCanceledException`: `cancellationToken` was cancelled before the work started.

### `RunInScopeAsync<TResult>(TKey, Func<ITenantScope<TKey>, CancellationToken, Task<TResult>>, CancellationToken)`

Looks the tenant up with [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), then runs `work` inside a new scope for it (see [`ITenantScopeFactory<TKey>.CreateScope`](tenantry-itenantscopefactory.md)). The scope is disposed when the work completes or throws. The caller's current tenant is never changed.

```csharp
Task<TResult> RunInScopeAsync<TResult>(TKey tenantId, Func<ITenantScope<TKey>, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
```

Type parameters:

- `TResult`: The type of the work's result.

Parameters:

- `tenantId` `TKey`: The id of the tenant to run the work as.
- `work` `Func<ITenantScope<TKey>, CancellationToken, Task<TResult>>`: The work to run. It receives the scope and `cancellationToken`.
- `cancellationToken` `CancellationToken`: Passed to the tenant lookup and to `work`.

Returns: `Task<TResult>`: The value returned by `work`.

Exceptions:

- [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md): The tenant store has no tenant with that id.
- [`TenantInactiveException`](tenantry-tenantinactiveexception.md): An [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) refused the tenant.
- `InvalidOperationException`: No tenant store is registered.
- `ArgumentException`: `tenantId` is the key type's default value or an empty string, which no tenant can have.
- `OperationCanceledException`: `cancellationToken` was cancelled before the work started.
