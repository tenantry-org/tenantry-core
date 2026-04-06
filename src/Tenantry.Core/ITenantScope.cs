namespace Tenantry.Core;

/// <summary>
/// Represents a scoped interface for managing tenant-specific context within the application.
/// It provides mechanisms to activate and maintain a tenant context within the current execution flow,
/// such as for HTTP requests or background tasks.
/// </summary>
/// <typeparam name="TKey">
/// The type used for tenant identification. This type must support equality comparison and parsing operations.
/// </typeparam>
public interface ITenantScope<TKey> : ITenantContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Activates a tenant for the current execution context (e.g. an HTTP request or background job).
    /// The returned <see cref="IDisposable"/> clears the tenant when disposed.
    /// </summary>
    /// <param name="tenant">The tenant to activate.</param>
    /// <returns>A handle that clears the tenant context on disposal.</returns>
    /// <exception cref="InvalidOperationException">A tenant scope is already active.</exception>
    IDisposable BeginScope(ITenantDescriptor<TKey> tenant);
}
