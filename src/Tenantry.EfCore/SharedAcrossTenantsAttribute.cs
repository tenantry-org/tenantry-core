namespace Tenantry.EfCore;

/// <summary>
/// Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself, so
/// <see cref="TenantModel.FindUnisolatedEntityTypes"/> does not report it. It changes nothing in queries or saves.
/// </summary>
/// <remarks>
/// Packages that keep tenants apart in a shared database by query filters alone, such as Tenantry.Pro's mixed mode,
/// refuse a model with an entity type that is neither tenant-owned nor marked this way. Mark it in the model instead
/// with <c>modelBuilder.Entity&lt;T&gt;().IsSharedAcrossTenants()</c>. An entity type that implements
/// <see cref="ITenantEntity{TKey}"/> cannot be marked.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute;
