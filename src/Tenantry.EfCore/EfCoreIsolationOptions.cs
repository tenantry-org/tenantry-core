namespace Tenantry.EfCore;

/// <summary>
/// Options for configuring EF Core tenant isolation registration.
/// Passed to <c>builder.AddEfCoreIsolation(options => ...)</c>.
/// </summary>
public sealed class EfCoreIsolationOptions
{
    /// <summary>
    /// When <see langword="true"/>, enables strict isolation mode:
    /// <list type="bullet">
    ///   <item><description>
    ///     Added entities with an explicitly-set non-current TenantId throw
    ///     <see cref="Tenantry.Core.Exceptions.TenantIsolationViolationException"/>
    ///     before any data is written (spoofing detection).
    ///   </description></item>
    ///   <item><description>
    ///     A missing-context detector logs a warning when SaveChanges runs
    ///     without a resolved tenant.
    ///   </description></item>
    /// </list>
    /// Without strict mode, the interceptor silently overwrites the TenantId on Added entities.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool StrictIsolation { get; set; }

    /// <summary>
    /// Additional configuration for strict isolation mode.
    /// These settings are applied only when <see cref="StrictIsolation"/> is enabled.
    /// </summary>
    public StrictIsolationOptions StrictIsolationOptions { get; } = new();
}
