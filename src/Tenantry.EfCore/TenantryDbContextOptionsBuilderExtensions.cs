using Microsoft.Extensions.DependencyInjection;
using Tenantry.EfCore.Internal;

// Extensions on DbContextOptionsBuilder live in its namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for wiring Tenantry into <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class TenantryDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds Tenantry interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Requires <c>AddEfCoreIsolation()</c> inside <c>AddTenantry</c>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder for the application's <see cref="DbContext"/>.</param>
    /// <param name="serviceProvider">
    /// The <see cref="IServiceProvider"/> from the <c>AddDbContext</c> factory callback,
    /// used to resolve the interceptor singleton.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">EF Core isolation is not registered (<c>AddEfCoreIsolation</c>).</exception>
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
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var configurator = serviceProvider.GetService<ITenantInterceptorConfigurator>()
            ?? throw new InvalidOperationException(
                "AddTenantInterceptors needs Tenantry's EF Core isolation, which is not registered. Add it in " +
                "AddTenantry: builder.Services.AddTenantry<TKey>(tenant => tenant.AddEfCoreIsolation()).");
        return configurator.AddInterceptors(optionsBuilder, serviceProvider);
    }
}
