using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Non-generic interface that enables <c>AddTenantInterceptors()</c> without repeating the TKey type parameter.
/// Registered during <c>AddEfCoreIsolation()</c> with a closed generic implementation.
/// </summary>
internal interface ITenantInterceptorConfigurator
{
    DbContextOptionsBuilder AddInterceptors(DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider);
}

internal sealed class TenantInterceptorConfigurator<TKey> : ITenantInterceptorConfigurator
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public DbContextOptionsBuilder AddInterceptors(DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        var interceptor = serviceProvider.GetRequiredService<TenantSaveChangesInterceptor<TKey>>();
        return optionsBuilder.AddInterceptors(interceptor);
    }
}
