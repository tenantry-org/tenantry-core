# `TenantResolvedContext<TKey>` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

The request whose tenant `app.UseTenantry()` made current, passed to [`TenantResolutionOptions<TKey>.OnResolved`](tenantry-aspnetcore-tenantresolutionoptions.md).

```csharp
public sealed class TenantResolvedContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Properties

### `HttpContext`

The request.

```csharp
public HttpContext HttpContext { get; }
```

Value: `HttpContext`

### `Tenant`

The request's tenant, now current.

```csharp
public ITenantDescriptor<TKey> Tenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md)
