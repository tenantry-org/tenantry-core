using System.Globalization;

namespace Tenantry;

/// <summary>
/// Thrown when work is to run for a tenant that an <see cref="ITenantActivityValidator{TKey}"/> refuses, such as a
/// suspended tenant, for example by
/// <see cref="ITenantScopeFactory{TKey}.RunInScopeAsync(TKey, Func{ITenantScope{TKey}, CancellationToken, Task}, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// It derives from <see cref="TenantNotResolvedException"/>, so code that handles that exception handles this one
/// too. Catch it first to skip or park work for a suspended tenant rather than treat it as a failure.
/// </remarks>
public sealed class TenantInactiveException : TenantNotResolvedException
{
    /// <summary>Initialises a new instance for the tenant that was refused.</summary>
    /// <param name="tenantId">The tenant's id, which the message formats with the invariant culture.</param>
    public TenantInactiveException(object tenantId)
        : base(string.Create(CultureInfo.InvariantCulture, $"Tenant '{tenantId}' is not active, so no work runs for it."))
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        TenantId = tenantId;
    }

    /// <summary>Initialises a new instance for the tenant that was refused, with a custom message.</summary>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="message">The message that describes the error.</param>
    public TenantInactiveException(object tenantId, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        TenantId = tenantId;
    }

    /// <summary>The tenant's id, of the application's tenant key type.</summary>
    public object TenantId { get; }
}
