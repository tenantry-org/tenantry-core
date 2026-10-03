# `SharedAcrossTenantsAttribute` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself, so [`TenantModel.FindUnisolatedEntityTypes`](tenantry-efcore-tenantmodel.md) does not report it. It changes nothing in queries or saves.

Packages that keep tenants apart in a shared database by query filters alone, such as Tenantry.Pro's mixed mode, refuse a model with an entity type that is neither tenant-owned nor marked this way. Mark it in the model instead with `modelBuilder.Entity<T>().IsSharedAcrossTenants()`. An entity type that implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md) cannot be marked.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute
```

Inherits `Attribute`.
