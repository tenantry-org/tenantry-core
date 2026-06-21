namespace Tenantry.Core.Exceptions;

/// <summary>
/// Thrown when a tenant could not be resolved from the current request context
/// and the operation requires a resolved tenant.
/// </summary>
public sealed class TenantNotResolvedException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new instance with a default message.
    /// </summary>
    public TenantNotResolvedException()
        : base("No tenant could be resolved from the current request. " +
               "Ensure the tenant resolution middleware is registered (app.UseTenantry()) " +
               "and that the request carries a valid tenant identifier.")
    {
    }

    /// <summary>
    /// Initialises a new instance with a custom message.
    /// </summary>
    public TenantNotResolvedException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with a custom message and inner exception.
    /// </summary>
    public TenantNotResolvedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
