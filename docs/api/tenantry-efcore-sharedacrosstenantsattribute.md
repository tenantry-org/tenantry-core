# `SharedAcrossTenantsAttribute` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself. It changes nothing in queries or saves.

`UseTenantry()` refuses a model that has tenant-owned entity types and also an entity type that is neither tenant-owned nor marked this way ([`EfCoreIsolationOptions.OnUnclassifiedEntityType`](tenantry-efcore-efcoreisolationoptions.md)). Mark it in the model instead with `modelBuilder.Entity<T>().IsSharedAcrossTenants()`. A derived type follows its base type. An entity type that implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md) cannot be marked.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute
```

Inherits `Attribute`.
