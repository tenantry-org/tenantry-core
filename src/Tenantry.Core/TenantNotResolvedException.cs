namespace Tenantry;

/// <summary>
/// Thrown when an operation needs a current tenant and none is current. Derived types cover a named tenant that does
/// not exist (<see cref="TenantNotFoundException"/>) or is inactive (<see cref="TenantInactiveException"/>).
/// </summary>
public class TenantNotResolvedException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new instance with a default message.
    /// </summary>
    public TenantNotResolvedException()
        : base("No tenant is current. Run tenant-scoped code while a tenant is current: in a request that " +
               "Tenantry resolved to a tenant (app.UseTenantry()), or inside a scope from ITenantScopeFactory " +
               "(RunInScopeAsync or CreateScope).")
    {
    }

    /// <summary>
    /// Initialises a new instance with a custom message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public TenantNotResolvedException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with a custom message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TenantNotResolvedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
