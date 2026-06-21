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
    /// Scopes may nest: an inner scope shadows the outer tenant, and disposing it restores the outer
    /// tenant (the outermost scope restores "no tenant"). The returned <see cref="IDisposable"/>
    /// restores the previously active tenant when disposed.
    /// </summary>
    /// <param name="tenant">The tenant to activate.</param>
    /// <returns>A handle that restores the previously active tenant on disposal.</returns>
    IDisposable BeginScope(ITenantDescriptor<TKey> tenant);
}
