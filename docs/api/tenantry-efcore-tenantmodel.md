# `TenantModel` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Reads which entity types of an EF Core model Tenantry isolates, for packages and tests that build on it.

```csharp
public static class TenantModel
```

## Fields

### `SharedAcrossTenantsAnnotation`

The model annotation `IsSharedAcrossTenants()` sets.

```csharp
public const string SharedAcrossTenantsAnnotation = "Tenantry:SharedAcrossTenants"
```

Returns: `string`

## Methods

### `FindUnisolatedEntityTypes(IReadOnlyModel)`

Returns the entity types of `model` that are neither tenant-owned nor marked as shared by every tenant. In a database that tenants share, Tenantry does not keep their rows apart.

```csharp
public static IReadOnlyList<IReadOnlyEntityType> FindUnisolatedEntityTypes(IReadOnlyModel model)
```

Parameters:

- `model` `IReadOnlyModel`: The model, such as `context.Model`.

Returns: `IReadOnlyList<IReadOnlyEntityType>`

### `HasTenantOwnedEntityTypes(IReadOnlyModel)`

Returns whether `model` has an entity type that implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md), which is what `UseTenantry()` isolates. A model without one has nothing for Tenantry's filters and checks to do.

```csharp
public static bool HasTenantOwnedEntityTypes(IReadOnlyModel model)
```

Parameters:

- `model` `IReadOnlyModel`: The model, such as `context.Model`.

Returns: `bool`

### `IsSharedAcrossTenants(IReadOnlyEntityType)`

Returns whether `entityType`, or the type that owns it, is marked as shared by every tenant, with [`SharedAcrossTenantsAttribute`](tenantry-efcore-sharedacrosstenantsattribute.md) or `IsSharedAcrossTenants()`.

```csharp
public static bool IsSharedAcrossTenants(IReadOnlyEntityType entityType)
```

Parameters:

- `entityType` `IReadOnlyEntityType`: The entity type.

Returns: `bool`

### `IsTenantOwned(IReadOnlyEntityType)`

Returns whether `entityType` is tenant-owned: it implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md), or it is an owned type whose owner is.

```csharp
public static bool IsTenantOwned(IReadOnlyEntityType entityType)
```

Parameters:

- `entityType` `IReadOnlyEntityType`: The entity type.

Returns: `bool`
