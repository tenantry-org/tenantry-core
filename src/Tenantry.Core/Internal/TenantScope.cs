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

        if (CurrentTenantLocal.Value is not null)
            throw new InvalidOperationException(
                "A tenant scope is already active. Nested scopes are not supported");

        CurrentTenantLocal.Value = tenant;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentTenantLocal.Value = null;
    }
}
