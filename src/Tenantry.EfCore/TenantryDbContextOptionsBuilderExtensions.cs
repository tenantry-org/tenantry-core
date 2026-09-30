using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore;
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
    /// Isolates the context's tenant-owned entities (those implementing <see cref="ITenantEntity{TKey}"/>) by the
    /// current tenant: queries return only the current tenant's rows, new entities are saved for the current tenant,
    /// saving a change to another tenant's entity throws, and <c>ExecuteUpdate</c> cannot set <c>TenantId</c>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder for the application's <see cref="DbContext"/>.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Any <see cref="DbContext"/> works, pooled or not, with no base class or interface. Tenantry adds the tenant
    /// query filter after <c>OnModelCreating</c>, so your own configuration can come in any order, and it combines
    /// the tenant filter with your own filters. On EF Core 10 and later the tenant filter is named
    /// <see cref="TenantryQueryFilters.Tenant"/>, unless the entity also has an unnamed filter, which it is merged
    /// into. It also makes each tenant-owned entity's <c>TenantId</c> a concurrency token, so every <c>UPDATE</c>
    /// and <c>DELETE</c> matches only a row stored under the tenant the entity was loaded as.
    /// </para>
    /// <para>
    /// The current tenant is read from the context's application service provider, which <c>AddDbContext</c>,
    /// <c>AddDbContextPool</c>, <c>AddDbContextFactory</c> and <c>AddPooledDbContextFactory</c> supply, so Tenantry
    /// must be registered there with <c>AddTenantry</c> for the tenant key type your entities use. A context
    /// without an application service provider (one built by hand without <c>UseApplicationServiceProvider</c>)
    /// builds its model, for design-time tools, but throws on its first query or save.
    /// </para>
    /// <para>
    /// Every <see cref="ITenantDbContextOptionsContributor"/> registered in the application service provider
    /// configures the options here, and every <see cref="ITenantModelContributor"/> the model; without an application
    /// service provider, none runs. Calling this again changes nothing.
    /// </para>
    /// <para>
    /// It installs Tenantry's own EF Core model customizer, so the options must not also replace
    /// <c>IModelCustomizer</c>, nor use <c>UseInternalServiceProvider</c>; creating such a context throws. A compiled
    /// model (<c>dotnet ef dbcontext optimize</c>) is not supported: EF Core compiles no model with query filters.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;(options =&gt; options
    ///     .UseSqlServer(connectionString)
    ///     .UseTenantry());
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseTenantry(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        if (optionsBuilder.Options.FindExtension<TenantryOptionsExtension>() is not null)
        {
            return optionsBuilder;
        }

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new TenantryOptionsExtension());
        optionsBuilder.AddInterceptors(TenantSaveChangesInterceptor.Instance, TenantBulkUpdateGuard.Instance);

        if (ApplicationServices.Find(optionsBuilder.Options) is { } services)
        {
            foreach (var contributor in services.GetServices<ITenantDbContextOptionsContributor>())
            {
                contributor.Configure(optionsBuilder);
            }
        }

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseTenantry(DbContextOptionsBuilder)"/>
    /// <typeparam name="TContext">The type of context being configured.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseTenantry<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTenantry((DbContextOptionsBuilder)optionsBuilder);
}
