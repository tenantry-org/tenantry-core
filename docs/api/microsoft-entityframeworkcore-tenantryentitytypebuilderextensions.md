# `TenantryEntityTypeBuilderExtensions` class

Namespace: `Microsoft.EntityFrameworkCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Marks entity types in the model for Tenantry.

```csharp
public static class TenantryEntityTypeBuilderExtensions
```

## Methods

### `IsSharedAcrossTenants(EntityTypeBuilder)`

Marks the entity type as one whose rows every tenant shares, as [`SharedAcrossTenantsAttribute`](tenantry-efcore-sharedacrosstenantsattribute.md) does. It changes nothing in queries or saves.

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static EntityTypeBuilder IsSharedAcrossTenants(this EntityTypeBuilder builder)
```

Parameters:

- `builder` `EntityTypeBuilder`: The entity type's builder.

Returns: `EntityTypeBuilder`: The same `builder` for chaining.

### `IsSharedAcrossTenants<TEntity>(EntityTypeBuilder<TEntity>)`

Marks the entity type as one whose rows every tenant shares, as [`SharedAcrossTenantsAttribute`](tenantry-efcore-sharedacrosstenantsattribute.md) does. It changes nothing in queries or saves.

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static EntityTypeBuilder<TEntity> IsSharedAcrossTenants<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class
```

Type parameters:

- `TEntity`: The entity type.

Parameters:

- `builder` `EntityTypeBuilder<TEntity>`: The entity type's builder.

Returns: `EntityTypeBuilder<TEntity>`: The same `builder` for chaining.
