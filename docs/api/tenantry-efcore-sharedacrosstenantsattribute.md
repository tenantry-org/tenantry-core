# `SharedAcrossTenantsAttribute` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself. It changes nothing in queries or saves.

Every entity type that is not tenant-owned is shared, marked or not. The marker states it, for an application that sets [`EfCoreIsolationOptions.OnUnmarkedEntityType`](tenantry-efcore-efcoreisolationoptions.md) to `Warn` or `Reject`, and for packages that read [`TenantModel.FindUnisolatedEntityTypes`](tenantry-efcore-tenantmodel.md). Mark it in the model instead with `modelBuilder.Entity<T>().IsSharedAcrossTenants()`. A derived type follows its base type. An entity type that implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md) cannot be marked.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute
```

Inherits `Attribute`.
