using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Extensions;

/// <summary>
/// DbContext pooling for applications that give each tenant its own database.
/// </summary>
public static class TenantDbContextPoolExtensions
{
    // What EF Core requires of a context type it creates (matches its own annotations).
    internal const DynamicallyAccessedMemberTypes ContextMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties;

    /// <summary>
    /// Pools <typeparamref name="TContext"/> for a database per tenant. Registers a scoped
    /// <typeparamref name="TContext"/> and <see cref="IDbContextFactory{TContext}"/>, both leasing from one pool,
    /// and connects every lease to the current tenant's database through
    /// <see cref="ITenantConnectionStringResolver{TKey}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pooled context keeps its connection string when it returns to the pool, so resolving the connection
    /// string in a regular <c>AddDbContextPool</c> callback (which runs once) would send every tenant to the
    /// first tenant's database. Here, configure the provider <em>without</em> a connection string; each lease
    /// gets the current tenant's. Leasing without a current tenant throws <c>TenantNotResolvedException</c>.
    /// </para>
    /// <para>
    /// A guard also checks each pooled context before it opens a connection and before every command it runs,
    /// including on a connection that is already open: the connection must have been set for the context's
    /// current lease and for the tenant that is current now. Otherwise it throws
    /// <c>TenantIsolationViolationException</c> rather than use another tenant's database.
    /// </para>
    /// <para>
    /// The scoped <typeparamref name="TContext"/> resolves the connection string synchronously, so it needs
    /// <see cref="TenantConnectionStringOptions{TKey}.GetConnectionString"/>. With only an asynchronous
    /// delegate, use <c>IDbContextFactory&lt;TContext&gt;.CreateDbContextAsync</c>. The context must have a
    /// constructor that takes only its options, as <see cref="MultiTenantDbContext{TKey}"/> does.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt;
    /// {
    ///     tenant.ResolveFromHeader("X-Tenant-Id");
    ///     tenant.UseStore&lt;AppTenantStore&gt;();
    ///     tenant.UseConnectionStrings(options =&gt;
    ///         options.GetConnectionString = t =&gt; $"Server=db;Database=app_{t.TenantId};Integrated Security=true");
    ///     tenant.AddEfCoreIsolation();
    /// });
    ///
    /// // No connection string here: each lease is connected to the current tenant's database.
    /// builder.Services.AddTenantDbContextPool&lt;AppDbContext, Guid&gt;((sp, options) =&gt;
    ///     options.UseSqlServer().AddTenantInterceptors(sp));
    /// </code>
    /// </example>
    public static IServiceCollection AddTenantDbContextPool<[DynamicallyAccessedMembers(ContextMembers)] TContext, TKey>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> optionsAction,
        int poolSize = 1024)
        where TContext : DbContext
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsAction);

        services.AddPooledDbContextFactory<TContext>(
            (sp, options) =>
            {
                // The guard goes first, so a context used under the wrong tenant is rejected before any other
                // interceptor acts on it (for example, stamping pending inserts with the current tenant).
                options.AddInterceptors(new TenantDatabaseGuard<TKey>(sp.GetRequiredService<ITenantContext<TKey>>()));
                optionsAction(sp, options);
            },
            poolSize);

        // Put a tenant-connecting factory in place of EF Core's pooled one, which it wraps. Anything that asks
        // for IDbContextFactory<TContext>, including the scoped registration below, then gets connected leases.
        var pooled = services.Last(d => d.ServiceType == typeof(IDbContextFactory<TContext>) && !d.IsKeyedService);
        services.Remove(pooled);
        services.AddSingleton<IDbContextFactory<TContext>>(sp => new TenantDatabaseDbContextFactory<TContext, TKey>(
            CreatePooledFactory<TContext>(sp, pooled),
            sp.GetService<ITenantConnectionStringResolver<TKey>>()
            ?? throw new InvalidOperationException(
                $"AddTenantDbContextPool<{typeof(TContext).Name}, {typeof(TKey).Name}> needs per-tenant connection " +
                "strings. Configure them with UseConnectionStrings when adding Tenantry."),
            sp.GetRequiredService<ITenantContext<TKey>>()));

        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());

        return services;
    }

    private static IDbContextFactory<TContext> CreatePooledFactory<[DynamicallyAccessedMembers(ContextMembers)] TContext>(IServiceProvider services, ServiceDescriptor pooled)
        where TContext : DbContext =>
        (IDbContextFactory<TContext>)(pooled.ImplementationInstance
                                      ?? pooled.ImplementationFactory?.Invoke(services)
                                      ?? ActivatorUtilities.CreateInstance(services, pooled.ImplementationType!));
}
