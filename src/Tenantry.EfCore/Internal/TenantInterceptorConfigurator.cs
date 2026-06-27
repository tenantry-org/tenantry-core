using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        // Idempotent: the interceptor can be wired from two places — the AddDbContext callback
        // (options.AddTenantInterceptors(sp)) and MultiTenantDbContext.OnConfiguring's self-wiring.
        // EF runs every registered SaveChangesInterceptor, so adding ours twice would double-stamp and
        // double-validate on each save. Skip if an instance of our interceptor is already present.
        var alreadyAdded = optionsBuilder.Options
            .FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<TenantSaveChangesInterceptor<TKey>>().Any() ?? false;

        if (alreadyAdded)
            return optionsBuilder;

        var interceptor = serviceProvider.GetRequiredService<TenantSaveChangesInterceptor<TKey>>();
        return optionsBuilder.AddInterceptors(interceptor);
    }
}
