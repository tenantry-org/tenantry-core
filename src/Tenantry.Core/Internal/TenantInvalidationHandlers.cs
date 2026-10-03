using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Runs every registered <see cref="ITenantInvalidationHandler{TKey}"/> for <see cref="ITenantStoreCache{TKey}"/>,
/// resolving them the first time, so a handler may depend on the cache itself.
/// </summary>
internal sealed class TenantInvalidationHandlers<TKey>(IServiceProvider services)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private ITenantInvalidationHandler<TKey>[]? _handlers;

    public void Invalidate(TKey tenantId) => RunEach(handler => handler.Invalidate(tenantId));

    // No tenant has an id reserved for "no tenant", and a handler could read an empty one as every tenant.
    public static void ThrowIfUnset(TKey tenantId)
    {
        if (tenantId is null)
            throw new ArgumentNullException(nameof(tenantId));

        if (TenantIds.IsUnset(tenantId))
        {
            throw new ArgumentException(
                $"'{tenantId}' is reserved for \"no tenant\" (the {typeof(TKey).Name} default value, or an empty string), " +
                "so no tenant has it to invalidate.",
                nameof(tenantId));
        }
    }

    public void InvalidateAll() => RunEach(handler => handler.InvalidateAll());

    private void RunEach(Action<ITenantInvalidationHandler<TKey>> run)
    {
        _handlers ??= services.GetServices<ITenantInvalidationHandler<TKey>>().ToArray();
        List<Exception>? errors = null;

        foreach (var handler in _handlers)
        {
            try
            {
                run(handler);
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
