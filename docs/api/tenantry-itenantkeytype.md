# `ITenantKeyType` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The tenant key type the application registered Tenantry with, for code that has only a service provider or a service collection, such as a health check registration or a host extension, so its callers never repeat the key type. `AddTenantry` registers it as a singleton; `services.FindTenantKeyType()` reads it while services are being registered.

[`ITenantKeyType.Accept<TResult>`](tenantry-itenantkeytype.md) calls a generic method with the key type known at compile time, so Native AOT compiles it, unlike `MakeGenericType` on [`ITenantKeyType.Type`](tenantry-itenantkeytype.md).

```csharp
public interface ITenantKeyType
```

## Properties

### `Type`

The tenant key type, such as `Guid`.

```csharp
Type Type { get; }
```

Value: `Type`

## Methods

### `Accept<TResult>(ITenantKeyTypeVisitor<TResult>)`

Calls `visitor` with the tenant key type.

```csharp
TResult Accept<TResult>(ITenantKeyTypeVisitor<TResult> visitor)
```

Type parameters:

- `TResult`: What the visitor returns.

Parameters:

- `visitor` [`ITenantKeyTypeVisitor<TResult>`](tenantry-itenantkeytypevisitor.md): The code that needs the key type.

Returns: `TResult`: What `visitor` returns.
