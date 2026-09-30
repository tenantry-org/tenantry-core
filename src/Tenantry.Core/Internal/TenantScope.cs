using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Default <see cref="ITenantScope{TKey}"/>: a dependency-injection scope plus the handle that made its tenant
/// current.
/// </summary>
internal sealed class TenantScope<TKey>(
    AsyncServiceScope services,
    IDisposable tenantUse,
    ITenantDescriptor<TKey> tenant)
    : ITenantScope<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    public IServiceProvider ServiceProvider => services.ServiceProvider;

    /// <inheritdoc />
    public ITenantDescriptor<TKey> Tenant => tenant;

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            services.Dispose();
        }
        finally
        {
            tenantUse.Dispose();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately not an <see langword="async"/> method. Changes an async method makes to an
    /// <see cref="AsyncLocal{T}"/> are undone when it returns, so a tenant restored after an <c>await</c>
    /// would never reach the caller. Instead this starts disposing the services while the tenant is still
    /// active (any asynchronous part of that disposal keeps seeing it), then restores the previous tenant
    /// here, in the caller's context, before returning.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        ValueTask disposal;

        try
        {
            disposal = services.DisposeAsync();
        }
        finally
        {
            tenantUse.Dispose();
        }

        return disposal;
    }
}
