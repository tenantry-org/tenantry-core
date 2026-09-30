using Microsoft.Extensions.DependencyInjection;

namespace Tenantry;

/// <summary>
/// A dependency-injection scope with a tenant current, created by <see cref="ITenantScopeFactory{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Disposing the scope (synchronously or asynchronously) disposes its services while the tenant is still
/// current, then restores the tenant that was current when the scope was created. Both forms restore the
/// tenant in the disposing code's own context, so <c>await using</c> is safe in loops and nested scopes.
/// </remarks>
public interface ITenantScope<TKey> : IServiceScope, IAsyncDisposable
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>The tenant this scope runs as.</summary>
    ITenantDescriptor<TKey> Tenant { get; }
}
