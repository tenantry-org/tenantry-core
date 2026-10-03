# `ITenantActivity<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Whether a tenant may have work run for it, as the registered [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md)s decide. A tenant is active when every validator allows it, and always when none is registered.

Registered as a singleton by `AddTenantry`. Tenantry consults it in [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md) and, in Tenantry.AspNetCore, for each request. Tenantry.Pro's background services, schedulers and message integrations consult it too.

[`ITenantScopeFactory<TKey>.CreateScope`](tenantry-itenantscopefactory.md) does not: it takes a tenant you already hold, for work such as provisioning and migrations that must reach suspended tenants. Check this first when you iterate tenants for work of your own.

```csharp
public interface ITenantActivity<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Methods

### `IsActiveAsync(ITenantDescriptor<TKey>, CancellationToken)`

Returns [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) when every validator allows `tenant`.

```csharp
ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant.
- `cancellationToken` `CancellationToken`: Cancels the check.

Returns: `ValueTask<bool>`

### `ThrowIfInactiveAsync(ITenantDescriptor<TKey>, CancellationToken)`

Throws [`TenantInactiveException`](tenantry-tenantinactiveexception.md) unless every validator allows `tenant`.

```csharp
ValueTask ThrowIfInactiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant.
- `cancellationToken` `CancellationToken`: Cancels the check.

Returns: `ValueTask`

Exceptions:

- [`TenantInactiveException`](tenantry-tenantinactiveexception.md): A validator refused the tenant.
