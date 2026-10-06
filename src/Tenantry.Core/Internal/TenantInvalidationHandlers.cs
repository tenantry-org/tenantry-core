using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Runs every registered <see cref="ITenantInvalidationHandler{TKey}"/>, then, when asked to, every broadcasting one
/// (<c>BroadcastInvalidations</c>, keyed with <see cref="BroadcastKey"/>). They are resolved the first time, so a handler
/// may depend on <see cref="ITenantInvalidator{TKey}"/> itself.
/// </summary>
internal sealed class TenantInvalidationHandlers<TKey>(IServiceProvider services)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public const string BroadcastKey = "Tenantry.Invalidation.Broadcast";

    private ITenantInvalidationHandler<TKey>[]? _handlers;
    private ITenantInvalidationHandler<TKey>[]? _broadcasters;

    public ValueTask InvalidateAsync(TKey tenantId, bool broadcast, CancellationToken cancellationToken) =>
        RunEachAsync(handler => handler.InvalidateAsync(tenantId, cancellationToken), broadcast, cancellationToken);

    public ValueTask InvalidateAllAsync(bool broadcast, CancellationToken cancellationToken) =>
        RunEachAsync(handler => handler.InvalidateAllAsync(cancellationToken), broadcast, cancellationToken);

    private async ValueTask RunEachAsync(
        Func<ITenantInvalidationHandler<TKey>, ValueTask> run,
        bool broadcast,
        CancellationToken cancellationToken)
    {
        _handlers ??= services.GetServices<ITenantInvalidationHandler<TKey>>().ToArray();
        var handlers = _handlers.AsEnumerable();

        if (broadcast)
        {
            _broadcasters ??= services.GetKeyedServices<ITenantInvalidationHandler<TKey>>(BroadcastKey).ToArray();
            handlers = handlers.Concat(_broadcasters);
        }

        List<Exception>? errors = null;

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await run(handler).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                (errors ??= []).Add(e);
            }
        }

        if (errors is [var single])
            ExceptionDispatchInfo.Throw(single);

        if (errors is not null)
            throw new AggregateException("More than one tenant invalidation handler threw.", errors);
    }
}
