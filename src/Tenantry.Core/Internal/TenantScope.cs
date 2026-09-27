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
    // The ambient value is the innermost open scope. Each scope links to the one it shadows, so closing
    // scopes in any order (or from another async flow) restores the nearest scope that is still open.
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    /// <inheritdoc />
    public ITenantDescriptor<TKey>? CurrentTenant => CurrentFrame.Value?.Tenant;

    /// <inheritdoc />
    public bool HasTenant => CurrentFrame.Value is not null;

    /// <inheritdoc />
    public TKey? CurrentTenantId => CurrentFrame.Value is { } frame ? frame.Tenant.TenantId : default;

    /// <inheritdoc />
    /// <remarks>
    /// Scopes nest: an inner scope shadows the outer one, and disposing it restores the outer tenant. The
    /// ambient value flows down into awaited callees, never back up to the caller. Disposing a scope that
    /// is not the innermost one in the current flow (out of order, or from a different async flow) only
    /// closes it; the innermost scope stays active, and when it closes the nearest scope that is still open
    /// is restored. Disposal restores the tenant only in the flow that disposes: if a child task disposes a
    /// handle it inherited, the caller keeps that tenant until it disposes the handle too, which then
    /// restores the caller's previous tenant. Further disposals change nothing.
    /// </remarks>
    public IDisposable BeginScope(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        Frame frame = new(tenant, CurrentFrame.Value);
        CurrentFrame.Value = frame;
        return frame;
    }

    private sealed class Frame(ITenantDescriptor<TKey> tenant, Frame? parent) : IDisposable
    {
        private int _disposed;

        public ITenantDescriptor<TKey> Tenant { get; } = tenant;

        private Frame? Parent { get; } = parent;

        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public void Dispose()
        {
            // Closing is shared by every flow, but the ambient value belongs to each flow. So every call, even a
            // repeat, restores the calling flow if its innermost scope is closed: this one, or one another flow
            // closed (for example a child task that disposed a handle it inherited).
            Volatile.Write(ref _disposed, 1);

            var current = CurrentFrame.Value;

            if (current is not { IsDisposed: true })
            {
                // No scope, or the innermost scope is still open: it stays active, and this scope is skipped
                // when it closes.
                return;
            }

            var restored = current.Parent;

            while (restored is { IsDisposed: true })
            {
                restored = restored.Parent;
            }

            CurrentFrame.Value = restored;
        }
    }
}
