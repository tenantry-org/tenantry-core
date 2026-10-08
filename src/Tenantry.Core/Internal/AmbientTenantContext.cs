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
    // The ambient value is the innermost scope of the flow. Each scope links to the one it shadows, so closing scopes
    // in any order (or from another async flow) restores the nearest scope that is still open, except where a scope was
    // opened over one already closed (MakeCurrent's remarks).
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    /// <inheritdoc />
    public ITenantDescriptor<TKey>? CurrentTenant => CurrentFrame.Value?.Tenant;

    /// <inheritdoc />
    public bool HasTenant => CurrentFrame.Value?.Tenant is not null;

    /// <inheritdoc />
    public TKey? CurrentTenantId => CurrentFrame.Value?.Tenant is { } tenant ? tenant.TenantId : default;

    /// <inheritdoc />
    /// <remarks>
    /// Scopes nest: an inner scope shadows the outer one, and disposing it restores the outer tenant. The ambient value
    /// flows down into awaited callees, never back up to the caller. Disposing a scope closes it for every flow, but
    /// changes the tenant only in the flow that disposes it, and only when that flow's innermost scope is then closed
    /// and is the disposed scope or one inside it; otherwise the innermost scope stays current. A child task that
    /// disposes a handle it inherited therefore leaves the caller on that tenant until the caller disposes it too.
    /// <para>
    /// When the tenant changes, the flow walks out from its innermost scope and gets the first scope still open, or no
    /// tenant. The walk stops early at a scope that was opened when the scope around it had already closed, and the
    /// flow gets that closed scope's tenant. This keeps a flow on its tenant after another flow closed the scope it
    /// started in, as a long-polling SignalR connection's hub calls do: each scope the flow opens while none of its own
    /// is open gives that tenant back when it closes. A flow started inside such a scope gets that tenant too, when it
    /// closes its own scope after that scope has closed.
    /// </para>
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
        // A parent already closed when this frame opened, such as the scope this flow started in, which another flow
        // closed (a long-polling SignalR connection runs its hub calls in the flow of a request that has ended). The
        // walk out of closed frames stops here and restores that parent, so the flow keeps its tenant.
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
