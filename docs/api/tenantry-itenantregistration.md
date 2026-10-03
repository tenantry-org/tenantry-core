# `ITenantRegistration` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

An extension point: for code that extends the package, such as another package that builds on it. An application rarely needs it.

A registration that needs the tenant key type, added through [`ITenantBuilder.Add`](tenantry-itenantbuilder.md). Packages use it for builder methods that take a type parameter of their own, such as a `DbContext` type.

```csharp
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantRegistration
```

## Methods

### `Apply<TKey>(ITenantBuilder<TKey>)`

Registers the feature's services for the tenant key type `TKey`.

```csharp
void Apply<TKey>(ITenantBuilder<TKey> tenant) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant key type of the builder.

Parameters:

- `tenant` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The builder the registration was added to.
