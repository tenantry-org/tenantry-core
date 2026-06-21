namespace Tenantry.Core.Internal;

/// <summary>
/// Singleton implementation of <see cref="ITenantScope{TKey}"/> backed by an
/// <see cref="AsyncLocal{T}"/> so each async execution context carries its own tenant
/// without creating a new accessor instance per request or operation.
/// </summary>
/// <remarks>
/// Registered as a singleton. In ASP.NET Core, middleware sets the tenant at the start of
/// each request and clears it in a finally block. In worker services, callers set and clear
/// it manually around their tenant-scoped operations.
/// </remarks>
internal sealed class TenantScope<TKey> : ITenantScope<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private static readonly AsyncLocal<ITenantDescriptor<TKey>?> CurrentTenantLocal = new();

    /// <inheritdoc />
    public ITenantDescriptor<TKey>? CurrentTenant => CurrentTenantLocal.Value;

    /// <inheritdoc />
    public bool HasTenant => CurrentTenantLocal.Value is not null;

    /// <inheritdoc />
    public TKey? CurrentTenantId => CurrentTenantLocal.Value is { } tenant ? tenant.TenantId : default;

    /// <inheritdoc />
    public IDisposable BeginScope(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Save the current tenant and restore it on dispose. This supports nested scopes: an inner
        // scope shadows the outer one, and the outer tenant is restored when the inner scope is
        // disposed. (The ambient value flows down into awaited callees, never back up to the caller.)
        var previous = CurrentTenantLocal.Value;
        CurrentTenantLocal.Value = tenant;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(ITenantDescriptor<TKey>? previous) : IDisposable
    {
        public void Dispose() => CurrentTenantLocal.Value = previous;
    }
}
