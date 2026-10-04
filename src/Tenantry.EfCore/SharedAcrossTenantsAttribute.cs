namespace Tenantry.EfCore;

/// <summary>
/// Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself. It changes
/// nothing in queries or saves.
/// </summary>
/// <remarks>
/// Every entity type that is not tenant-owned is shared, marked or not. The marker states it, for an application that
/// sets <see cref="EfCoreIsolationOptions.OnUnmarkedEntityType"/> to <c>Warn</c> or <c>Reject</c>, and for packages
/// that read <see cref="TenantModel.FindUnisolatedEntityTypes"/>. Mark it in the model instead with
/// <c>modelBuilder.Entity&lt;T&gt;().IsSharedAcrossTenants()</c>. A derived type follows its base type. An entity type
/// that implements <see cref="ITenantEntity{TKey}"/> cannot be marked.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute;
