using Tenantry.Core;

namespace Tenantry.EfCore;

/// <summary>
/// Options for configuring EF Core tenant isolation registration.
/// Passed to <c>builder.AddEfCoreIsolation(options => ...)</c>.
/// </summary>
/// <remarks>
/// Two protections are <strong>always</strong> on, independent of these options: reads fail closed
/// (query filters match nothing when no tenant is resolved), and <c>Modified</c>/<c>Deleted</c>
/// entities belonging to a different tenant are rejected before <c>SaveChanges</c> persists anything.
/// These options govern the remaining behaviours.
/// </remarks>
public sealed class EfCoreIsolationOptions
{
    /// <summary>
    /// What happens when <c>SaveChanges</c> runs without a resolved tenant context.
    /// <list type="bullet">
    ///   <item><description><see cref="MissingTenantBehavior.Allow" /> — save without stamping a tenant, silently.</description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Warn" /> — save without stamping a tenant and log a warning. <strong>Default.</strong></description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Reject" /> — throw <see cref="Core.Exceptions.TenantNotResolvedException" /> before persisting.</description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Skip" /> — behaves like <see cref="MissingTenantBehavior.Allow" /> for writes (there is nothing to skip).</description></item>
    /// </list>
    /// Reads are unaffected — they always fail closed regardless of this setting.
    /// </summary>
    public MissingTenantBehavior OnMissingTenant { get; set; } = MissingTenantBehavior.Warn;

    /// <summary>
    /// When <see langword="true" />, an <c>Added</c> entity that carries an explicitly-set
    /// <c>TenantId</c> belonging to a tenant other than the current one throws
    /// <see cref="Core.Exceptions.TenantIsolationViolationException" /> before any data is written
    /// (spoofing detection). When <see langword="false" /> (the default), such a value is silently
    /// overwritten with the current tenant by the stamping interceptor.
    /// </summary>
    public bool DetectSpoofedWrites { get; set; }
}
