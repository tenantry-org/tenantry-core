namespace Tenantry.Internal;

/// <summary>
/// Singleton implementation of <see cref="ITenantContextSetter{TKey}"/> backed by an
/// <see cref="AsyncLocal{T}"/> so each async execution context carries its own tenant
/// without creating a new accessor instance per request or operation.
/// </summary>
/// <remarks>
/// Registered as a singleton. In ASP.NET Core, middleware sets the tenant at the start of
/// each request and clears it in a finally block. In worker services, callers set and clear
/// it manually around their tenant-scoped operations.
/// </remarks>
internal sealed class AmbientTenantContext<TKey> : ITenantContextSetter<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // The ambient value is the innermost open scope. Each scope links to the one it shadows, so closing
    // scopes in any order (or from another async flow) restores the nearest scope that is still open, or the
    // scope the flow started in when another flow closed that one first.
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    /// <inheritdoc />
    public ITenantDescriptor<TKey>? CurrentTenant => CurrentFrame.Value?.Tenant;

    /// <inheritdoc />
    public bool HasTenant => CurrentFrame.Value?.Tenant is not null;

    /// <inheritdoc />
    public TKey? CurrentTenantId => CurrentFrame.Value?.Tenant is { } tenant ? tenant.TenantId : default;

    /// <inheritdoc />
    /// <remarks>
    /// Scopes nest: an inner scope shadows the outer one, and disposing it restores the outer tenant. The
    /// ambient value flows down into awaited callees, never back up to the caller. Disposing a scope that
    /// is not the innermost one in the current flow (out of order, or from a different async flow) only
    /// closes it; the innermost scope stays active, and when it closes the nearest scope that is still open
    /// is restored. Disposal restores the tenant only in the flow that disposes: if a child task disposes a
    /// handle it inherited, the caller keeps that tenant until it disposes the handle too, which then
    /// restores the caller's previous tenant. Further disposals change nothing. A flow that runs on after
    /// another flow closed the scope it started in keeps that scope's tenant, and closing a scope it opened
    /// after that restores it. When the other flow closes that scope while the flow has one of its own open,
    /// closing the flow's scope restores the nearest scope still open instead.
    /// </remarks>
    public IDisposable MakeCurrent(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        TenantIds.ThrowIfReserved(tenant, nameof(tenant));

        Frame frame = new(tenant, CurrentFrame.Value);
        CurrentFrame.Value = frame;
        return frame;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A scope like any other, with no tenant: it nests, and closes in any order, as <see cref="MakeCurrent"/>'s do.
    /// </remarks>
    public IDisposable MakeNoTenantCurrent()
    {
        Frame frame = new(null, CurrentFrame.Value);
        CurrentFrame.Value = frame;
        return frame;
    }

    // A frame with no tenant is one MakeNoTenantCurrent opened.
    private sealed class Frame(ITenantDescriptor<TKey>? tenant, Frame? parent) : IDisposable
    {
        // A parent already closed when this frame opened is the scope this flow started in, which another flow closed
        // (a long-polling SignalR connection runs its hub calls in the flow of a request that has ended). Closing this
        // frame restores it, so the flow keeps its tenant.
        private readonly bool _parentWasClosed = parent is { IsDisposed: true };

        private int _disposed;

        public ITenantDescriptor<TKey>? Tenant { get; } = tenant;

        private Frame? Parent { get; } = parent;

        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public void Dispose()
        {
            // Closing is shared by every flow, but the ambient value belongs to each flow. So every call, even a
            // repeat, restores the calling flow if its innermost scope is closed and this scope is that one or encloses
            // it: the innermost scope may be one another flow closed (for example a child task that disposed a handle
            // it inherited).
            Volatile.Write(ref _disposed, 1);

            var current = CurrentFrame.Value;

            if (current is not { IsDisposed: true } || !current.IsWithin(this))
            {
                // No scope, the innermost scope is still open (it stays active, and this scope is skipped when it
                // closes), or this scope is not one of the calling flow's.
                return;
            }

            // Scopes closed after the scope inside them opened are skipped; one closed before is restored.
            var closed = current;

            while (closed is { _parentWasClosed: false, Parent: { IsDisposed: true } parent })
            {
                closed = parent;
            }

            CurrentFrame.Value = closed.Parent;
        }

        private bool IsWithin(Frame scope)
        {
            for (var frame = this; frame is not null; frame = frame.Parent)
            {
                if (ReferenceEquals(frame, scope))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
