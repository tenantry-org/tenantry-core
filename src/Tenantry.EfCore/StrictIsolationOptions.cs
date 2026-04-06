namespace Tenantry.EfCore;

/// <summary>
/// Additional configuration for strict tenant isolation mode.
/// </summary>
public sealed class StrictIsolationOptions
{
    /// <summary>
    /// When <c>true</c> (default), <c>SaveChanges</c> calls that execute without a resolved tenant
    /// context log a structured warning. Useful for catching endpoints or background work that
    /// bypassed tenant propagation.
    /// </summary>
    public bool WarnOnMissingContext { get; set; } = true;
}
