# `ITenantScopeFactory<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Opens tenant scopes for work that runs outside an HTTP request: hosted services, queue consumers, scheduled jobs and console tools. Each scope pairs a fresh dependency-injection scope with an active tenant, so scoped services such as a `DbContext` are created for that tenant and isolated to it.

Registered as a singleton by `AddTenantryCore` and `AddTenantry`, so hosted services can take it as a constructor dependency. There are two ways to use it:

- You already hold the tenant (for example while iterating [`ITenantStoreAccessor<TKey>.GetAllTenantsAsync`](tenantry-core-itenantstoreaccessor.md)): `await using var scope = scopes.CreateScope(tenant);`
- You only have its id (for example from a queue message): `await scopes.RunInScopeAsync(tenantId, async (scope, ct) => { … }, ct);`

There is deliberately no `CreateScopeAsync(tenantId)`. The tenant is held in an `AsyncLocal<T>`, and changes an [async](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/async) method makes to one never reach its caller, so a scope opened inside an asynchronous lookup would not be active for the code that awaited it. [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-core-itenantscopefactory.md) does the lookup and then runs your work inside the scope instead.

```csharp
public interface ITenantScopeFactory<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

## Methods

### `CreateScope(ITenantDescriptor<TKey>)`

Creates a dependency-injection scope and makes `tenant` the current tenant for the calling code until the returned scope is disposed. Services resolved from `ServiceProvider` are created in the new scope and see this tenant.

```csharp
ITenantServiceScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant to activate.

Returns: [`ITenantServiceScope<TKey>`](tenantry-core-itenantservicescope.md): The scope. Dispose it (`using` or `await using`) to dispose its services and restore the tenant that was current before it was created.

### `RunInScopeAsync(TKey, Func<ITenantServiceScope<TKey>, CancellationToken, Task>, CancellationToken)`

Looks the tenant up with [`ITenantStoreAccessor<TKey>`](tenantry-core-itenantstoreaccessor.md), then runs `work` inside a new scope for it (see [`ITenantScopeFactory<TKey>.CreateScope`](tenantry-core-itenantscopefactory.md)). The scope is disposed when the work completes or throws. The caller's current tenant is never changed.

```csharp
Task RunInScopeAsync(TKey tenantId, Func<ITenantServiceScope<TKey>, CancellationToken, Task> work, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant to run the work as.
- `work` `Func<ITenantServiceScope<TKey>, CancellationToken, Task>`: The work to run. It receives the scope and `cancellationToken`.
- `cancellationToken` `CancellationToken`: Passed to the tenant lookup and to `work`.

Returns: `Task`

Exceptions:

- [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md): The tenant store has no tenant with that id.
- `OperationCanceledException`: `cancellationToken` was cancelled before the work started.

### `RunInScopeAsync<TResult>(TKey, Func<ITenantServiceScope<TKey>, CancellationToken, Task<TResult>>, CancellationToken)`

Looks the tenant up with [`ITenantStoreAccessor<TKey>`](tenantry-core-itenantstoreaccessor.md), then runs `work` inside a new scope for it (see [`ITenantScopeFactory<TKey>.CreateScope`](tenantry-core-itenantscopefactory.md)). The scope is disposed when the work completes or throws. The caller's current tenant is never changed.

```csharp
Task<TResult> RunInScopeAsync<TResult>(TKey tenantId, Func<ITenantServiceScope<TKey>, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
```

Type parameters:

- `TResult`: The type of the work's result.

Parameters:

- `tenantId` `TKey`: The id of the tenant to run the work as.
- `work` `Func<ITenantServiceScope<TKey>, CancellationToken, Task<TResult>>`: The work to run. It receives the scope and `cancellationToken`.
- `cancellationToken` `CancellationToken`: Passed to the tenant lookup and to `work`.

Returns: `Task<TResult>`: The value returned by `work`.

Exceptions:

- [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md): The tenant store has no tenant with that id.
- `OperationCanceledException`: `cancellationToken` was cancelled before the work started.
