using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Extensions;

/// <summary>
/// Extension methods for wiring Tenantry into <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds Tenantry interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Requires <c>AddEfCoreIsolation()</c> to have been called inside <c>AddTenantryCore()</c>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder for the application's <see cref="DbContext"/>.</param>
    /// <param name="serviceProvider">
    /// The <see cref="IServiceProvider"/> from the <c>AddDbContext</c> factory callback,
    /// used to resolve the interceptor singleton.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, options) =>
    ///     options.UseSqlServer(connectionString)
    ///            .AddTenantInterceptors(sp));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder AddTenantInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        var configurator = serviceProvider.GetRequiredService<ITenantInterceptorConfigurator>();
        return configurator.AddInterceptors(optionsBuilder, serviceProvider);
    }
}
