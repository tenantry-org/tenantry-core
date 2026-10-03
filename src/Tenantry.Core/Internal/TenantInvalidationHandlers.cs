using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Runs every registered <see cref="ITenantInvalidationHandler{TKey}"/>, resolving them the first time, so a handler may
/// depend on <see cref="ITenantInvalidator{TKey}"/> or <see cref="ITenantStoreCache{TKey}"/> itself.
/// </summary>
internal sealed class TenantInvalidationHandlers<TKey>(IServiceProvider services)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private ITenantInvalidationHandler<TKey>[]? _handlers;

    public ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken) =>
        RunEachAsync(handler => handler.InvalidateAsync(tenantId, cancellationToken), cancellationToken);

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
        RunEachAsync(handler => handler.InvalidateAllAsync(cancellationToken), cancellationToken);

    // ITenantStoreCache's synchronous methods wait for the handlers.
    public void Invalidate(TKey tenantId) => Wait(InvalidateAsync(tenantId, CancellationToken.None));

    public void InvalidateAll() => Wait(InvalidateAllAsync(CancellationToken.None));

    // No tenant has an id reserved for "no tenant", and a handler could read an empty one as every tenant.
    public static void ThrowIfReserved(TKey tenantId)
    {
        if (tenantId is null)
            throw new ArgumentNullException(nameof(tenantId));

        if (TenantIds.IsReserved(tenantId))
        {
            throw new ArgumentException(
                $"'{tenantId}' is reserved for \"no tenant\" (the {typeof(TKey).Name} default value, or an empty string), " +
                "so no tenant has it to invalidate.",
                nameof(tenantId));
        }
    }

    private static void Wait(ValueTask task)
    {
        if (!task.IsCompletedSuccessfully)
            task.AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask RunEachAsync(Func<ITenantInvalidationHandler<TKey>, ValueTask> run, CancellationToken cancellationToken)
    {
        _handlers ??= services.GetServices<ITenantInvalidationHandler<TKey>>().ToArray();
        List<Exception>? errors = null;

        foreach (var handler in _handlers)
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
