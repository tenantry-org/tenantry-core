namespace Tenantry;

/// <summary>
/// Thrown when a tenant is looked up by its id and the tenant store has no tenant with that id, for example by
/// <see cref="ITenantScopeFactory{TKey}.RunInScopeAsync(TKey, Func{ITenantScope{TKey}, CancellationToken, Task}, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// It derives from <see cref="TenantNotResolvedException"/>, so code that handles that exception handles this one
/// too. Catch it first to treat a message or job for a tenant that no longer exists differently from code that runs
/// without a tenant.
/// </remarks>
public sealed class TenantNotFoundException : TenantNotResolvedException
{
    /// <summary>
    /// Initialises a new instance for the tenant id that was not found.
    /// </summary>
    /// <param name="tenantId">The id that was looked up.</param>
    public TenantNotFoundException(object tenantId)
        : base($"Tenant '{tenantId}' was not found in the tenant store.")
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        TenantId = tenantId;
    }

    /// <summary>
    /// Initialises a new instance for the tenant id that was not found, with a custom message.
    /// </summary>
    /// <param name="tenantId">The id that was looked up.</param>
    /// <param name="message">The message that describes the error.</param>
    public TenantNotFoundException(object tenantId, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        TenantId = tenantId;
    }

    /// <summary>The id that was looked up, of the application's tenant key type.</summary>
    public object TenantId { get; }
}
