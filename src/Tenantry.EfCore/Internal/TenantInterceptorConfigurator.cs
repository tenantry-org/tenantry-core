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
        // Idempotent: the interceptors can be wired from two places — the AddDbContext callback
        // (options.AddTenantInterceptors(sp)) and MultiTenantDbContext.OnConfiguring's self-wiring.
        // EF runs every registered interceptor, so adding ours twice would double-stamp and
        // double-validate on each save. Skip any that are already present.
        var existing = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.ToList() ?? [];

        if (!existing.OfType<TenantSaveChangesInterceptor<TKey>>().Any())
            optionsBuilder.AddInterceptors(serviceProvider.GetRequiredService<TenantSaveChangesInterceptor<TKey>>());

        if (!existing.OfType<TenantBulkUpdateGuard<TKey>>().Any())
            optionsBuilder.AddInterceptors(TenantBulkUpdateGuard<TKey>.Instance);

        return optionsBuilder;
    }
}
