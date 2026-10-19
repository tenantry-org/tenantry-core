namespace Tenantry;

/// <summary>
/// Marks an entity type whose rows every tenant shares, such as a country list or the tenant table itself. It changes
/// nothing in queries or saves.
/// </summary>
/// <remarks>
/// Every entity type that is not tenant-owned is shared, marked or not. The marker states it, for an integration that
/// checks for entity types left unmarked by mistake: Tenantry.EfCore does when <c>OnUnmarkedEntityType</c> is
/// <c>Warn</c> or <c>Reject</c>, and lets a type be marked in the model instead, with
/// <c>modelBuilder.Entity&lt;T&gt;().IsSharedAcrossTenants()</c>. A derived type follows its base type. An entity type
/// that implements <see cref="ITenantEntity{TKey}"/> cannot be marked.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SharedAcrossTenantsAttribute : Attribute;
