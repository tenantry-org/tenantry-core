using Tenantry.Core;

namespace Tenantry.EfCore;

/// <summary>
/// Options for configuring EF Core tenant isolation registration.
/// Passed to <c>builder.AddEfCoreIsolation(options => ...)</c>.
/// </summary>
/// <remarks>
/// These protections are <strong>always</strong> on, independent of these options: reads fail closed
/// (query filters match nothing when no tenant is resolved); <c>Modified</c>/<c>Deleted</c> entities must
/// belong to the current tenant, checked before saving and again by the stored tenant in each
/// <c>UPDATE</c>/<c>DELETE</c>; and <c>ExecuteUpdate</c> cannot set <c>TenantId</c>. These options govern
/// writes without a tenant and inserts that name another tenant. Raw SQL and <c>IgnoreQueryFilters()</c>
/// are outside Tenantry's isolation.
/// </remarks>
public sealed class EfCoreIsolationOptions
{
    private MissingTenantBehavior _onMissingTenant = MissingTenantBehavior.Reject;

    /// <summary>
    /// What happens when <c>SaveChanges</c> writes <see cref="ITenantScoped{TKey}" /> entities without a
    /// resolved tenant. Saves that write no tenant-scoped entity are never affected.
    /// <list type="bullet">
    ///   <item><description><see cref="MissingTenantBehavior.Reject" /> — throw <see cref="Core.Exceptions.TenantNotResolvedException" /> before persisting. <strong>Default.</strong></description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Warn" /> — allow the write and log a warning.</description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Allow" /> — allow the write silently.</description></item>
    /// </list>
    /// <see cref="MissingTenantBehavior.Warn" /> and <see cref="MissingTenantBehavior.Allow" /> are for
    /// maintenance code that deliberately writes across tenants: updates and deletes are then not
    /// tenant-checked, and a new entity must set its <c>TenantId</c> explicitly, because an unowned row is
    /// always rejected. Reads always fail closed, whatever this setting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is <see cref="MissingTenantBehavior.Skip" /> (which only applies to background-job
    /// propagation) or not a defined value.
    /// </exception>
    public MissingTenantBehavior OnMissingTenant
    {
        get => _onMissingTenant;
        set => _onMissingTenant = value is MissingTenantBehavior.Reject or MissingTenantBehavior.Warn or MissingTenantBehavior.Allow
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "OnMissingTenant must be Reject, Warn or Allow. Skip only applies to background-job propagation.");
    }

    /// <summary>
    /// When <see langword="true" />, an <c>Added</c> entity that carries an explicitly-set
    /// <c>TenantId</c> belonging to a tenant other than the current one throws
    /// <see cref="Core.Exceptions.TenantIsolationViolationException" /> before any data is written
    /// (spoofing detection). When <see langword="false" /> (the default), such a value is silently
    /// overwritten with the current tenant by the stamping interceptor.
    /// </summary>
    public bool DetectSpoofedWrites { get; set; }
}
