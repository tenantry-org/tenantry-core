using Tenantry.Core;

namespace Tenantry.Samples.EfCoreConsole;

/// <summary>
/// A tenant-owned entity. Implementing <see cref="ITenantScoped{TKey}"/> (here via the
/// convenience base class <see cref="TenantScoped{TKey}"/>) is the only thing that opts an
/// entity into tenant isolation: the interceptor stamps <c>TenantId</c> on insert and the
/// global query filter restricts reads to the current tenant.
/// </summary>
public sealed class Order : TenantScoped<Guid>
{
    public int Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
