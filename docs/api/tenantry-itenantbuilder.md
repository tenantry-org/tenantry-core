# `ITenantBuilder` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The builder `AddTenantry` passes to its configuration callback, without the tenant key type. Features that take a type parameter of their own register through [`ITenantBuilder.Add`](tenantry-itenantbuilder.md), so their callers never repeat the key type.

```csharp
public interface ITenantBuilder
```

## Properties

### `Services`

Gets the application's service collection.

```csharp
IServiceCollection Services { get; }
```

Value: `IServiceCollection`

## Methods

### `Add(ITenantRegistration)`

Applies `registration` with the tenant key type this builder was created for.

```csharp
[EditorBrowsable(EditorBrowsableState.Advanced)]
void Add(ITenantRegistration registration)
```

Parameters:

- `registration` [`ITenantRegistration`](tenantry-itenantregistration.md): The registration to apply.
