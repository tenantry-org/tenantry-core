namespace Tenantry.EfCore;

/// <summary>
/// Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself. It changes
/// nothing in queries or saves.
/// </summary>
/// <remarks>
/// <c>UseTenantry()</c> refuses a model that has tenant-owned entity types and also an entity type that is neither
/// tenant-owned nor marked this way (<see cref="EfCoreIsolationOptions.OnUnclassifiedEntityType"/>). Mark it in the
/// model instead with <c>modelBuilder.Entity&lt;T&gt;().IsSharedAcrossTenants()</c>. A derived type follows its base
/// type. An entity type that implements <see cref="ITenantEntity{TKey}"/> cannot be marked.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute;
