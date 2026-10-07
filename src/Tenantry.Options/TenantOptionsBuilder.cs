using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tenantry.Options.Internal;

namespace Tenantry.Options;

/// <summary>
/// Sets options per tenant, in <c>tenant.ConfigurePerTenant(...)</c>: each <c>Configure</c> or <c>ConfigureAll</c> names
/// an options type and what differs for each tenant.
/// </summary>
/// <remarks>
/// <para>
/// <c>IOptionsSnapshot&lt;TOptions&gt;</c> and <c>IOptionsMonitor&lt;TOptions&gt;</c> then give the current tenant's
/// value, built from the ordinary configuration, then these steps with the tenant. Without a tenant they give the
/// ordinary value. <c>IOptions&lt;TOptions&gt;</c> always gives the ordinary value.
/// </para>
/// <para>
/// <c>IOptions&lt;TOptions&gt;</c> is not per tenant because its value is read once and kept, often in a singleton's
/// constructor. Read <c>IOptionsSnapshot&lt;TOptions&gt;</c>, which is scoped, in request code, and in a singleton
/// hold <c>IOptionsMonitor&lt;TOptions&gt;</c> and read <c>CurrentValue</c> each time, not once in the constructor.
/// The first read of <c>IOptions&lt;TOptions&gt;</c> while a tenant is
/// current logs a warning, event 2008 in the category <c>Tenantry.Options</c>.
/// </para>
/// <para>
/// Each tenant's value is built on first use and cached until <see cref="ITenantInvalidator{TKey}.InvalidateAsync"/>
/// clears it, so invalidate a tenant after changing its settings. With a store, the value is built from the store's
/// copy of the tenant, read once per value built, not from the copy that is current; a value for an id the store does
/// not hold is built from the current copy on every read and not kept. When the store answers with a tenant whose id
/// differs from the one asked for, as a store that matches ids without regard to case can, the value is built from the
/// store's copy and not kept either, since invalidating the store's id would not clear it. Without a store, the value
/// is built from the current copy and kept. A change to the configuration the options are bound to clears every
/// tenant's value. Validation (<c>Validate</c>, <c>IValidateOptions</c>) runs on each tenant's value when it is built,
/// and <c>ValidateOnStart</c> validates the ordinary one. The tenant's steps run after every <c>Configure</c> and
/// before every <c>PostConfigure</c>, in the order they are added.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public sealed class TenantOptionsBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private const string DefaultName = "";

    private readonly IServiceCollection _services;

    internal TenantOptionsBuilder(IServiceCollection services) => _services = services;

    /// <summary>Sets the default-named <typeparamref name="TOptions"/> per tenant.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configure">Sets the tenant's values, with the tenant (read your tenant type with <c>As&lt;T&gt;()</c>).</param>
    /// <returns>This builder, for more options types.</returns>
    /// <example>
    /// <code>
    /// tenant.ConfigurePerTenant(perTenant =&gt; perTenant
    ///     .Configure&lt;BrandingOptions&gt;((options, t) =&gt; options.Colour = t.As&lt;AppTenant&gt;().BrandColour));
    /// </code>
    /// </example>
    public TenantOptionsBuilder<TKey> Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions, ITenantDescriptor<TKey>> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(DefaultName, (options, tenant, _) => configure(options, tenant), withServices: false);
    }

    /// <summary>
    /// Sets the <typeparamref name="TOptions"/> named <paramref name="name"/> per tenant: an authentication scheme's, for
    /// example, which its handler reads with <c>IOptionsMonitor&lt;TOptions&gt;.Get(scheme)</c>.
    /// </summary>
    /// <remarks>
    /// The steps run after every <c>Configure</c> and before every <c>PostConfigure</c>, so an authentication handler's
    /// post-configuration (which builds JWT bearer's and OpenID Connect's metadata manager from <c>Authority</c>, and
    /// the scheme's data protector) sees the tenant's values. For authentication, the tenant must be current before the
    /// authentication middleware runs: see <c>app.UseTenantResolution()</c>.
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="name">The options' name, such as an authentication scheme's.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant.</param>
    /// <returns>This builder, for more options types.</returns>
    /// <example>
    /// <code>
    /// tenant.ConfigurePerTenant(perTenant =&gt; perTenant
    ///     .Configure&lt;JwtBearerOptions&gt;(JwtBearerDefaults.AuthenticationScheme, (options, t) =&gt;
    ///         options.Authority = t.As&lt;AppTenant&gt;().Authority));
    /// </code>
    /// </example>
    public TenantOptionsBuilder<TKey> Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        string name, Action<TOptions, ITenantDescriptor<TKey>> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(name, (options, tenant, _) => configure(options, tenant), withServices: false);
    }

    /// <summary>
    /// Sets the default-named <typeparamref name="TOptions"/> per tenant with the services of a scope created for the
    /// step: for settings read from a database. The scope is disposed after the step, and the tenant is current while it
    /// runs.
    /// </summary>
    /// <remarks>
    /// Options have no asynchronous configuration, so the step runs synchronously, once per tenant until the tenant is
    /// invalidated. A step that throws runs again on the next read.
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>This builder, for more options types.</returns>
    /// <example>
    /// <code>
    /// tenant.ConfigurePerTenant(perTenant =&gt; perTenant
    ///     .Configure&lt;BrandingOptions&gt;((options, t, services) =&gt;
    ///         options.Colour = services.GetRequiredService&lt;AppDbContext&gt;().Settings.Single(s =&gt; s.Key == "colour").Value));
    /// </code>
    /// </example>
    public TenantOptionsBuilder<TKey> Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(DefaultName, (options, tenant, services) => configure(options, tenant, services!), withServices: true);
    }

    /// <summary>
    /// Sets the <typeparamref name="TOptions"/> named <paramref name="name"/> per tenant with the services of a scope
    /// created for the step.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="name">The options' name, such as an authentication scheme's.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>This builder, for more options types.</returns>
    public TenantOptionsBuilder<TKey> Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        string name, Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(name, (options, tenant, services) => configure(options, tenant, services!), withServices: true);
    }

    /// <summary>
    /// Sets <typeparamref name="TOptions"/> per tenant for the default options and every named instance: every
    /// authentication scheme of the type, for example.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configure">Sets the tenant's values, with the tenant.</param>
    /// <returns>This builder, for more options types.</returns>
    public TenantOptionsBuilder<TKey> ConfigureAll<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions, ITenantDescriptor<TKey>> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(null, (options, tenant, _) => configure(options, tenant), withServices: false);
    }

    /// <summary>
    /// Sets <typeparamref name="TOptions"/> per tenant for the default options and every named instance, with the
    /// services of a scope created for the step.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>This builder, for more options types.</returns>
    public TenantOptionsBuilder<TKey> ConfigureAll<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Add<TOptions>(null, (options, tenant, services) => configure(options, tenant, services!), withServices: true);
    }

    // Name null: every name.
    private TenantOptionsBuilder<TKey> Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        string? name, Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider?> configure, bool withServices)
        where TOptions : class
    {
        var services = _services;
        services.AddOptions();

        services.TryAddSingleton<TenantOptionsCaches>();
        services.TryAddSingleton<ICurrentTenantId>(sp => new CurrentTenantId<TKey>(
            sp.GetRequiredService<ITenantContextSetter<TKey>>(), sp));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<TKey>, TenantOptionsInvalidation<TKey>>());

        // The options type's own cache and readers, once; IOptionsMonitor<TOptions> reads the cache too.
        services.TryAddSingleton(sp => new TenantOptionsCache<TOptions>(
            sp.GetRequiredService<ICurrentTenantId>(),
            sp.GetRequiredService<TenantOptionsCaches>(),
            sp.GetServices<IOptionsChangeTokenSource<TOptions>>()));
        services.TryAddSingleton<IOptionsMonitorCache<TOptions>>(sp => sp.GetRequiredService<TenantOptionsCache<TOptions>>());

        // IOptions<TOptions> stays the ordinary value: a singleton that reads it once must not keep a tenant's. Its
        // only message is event 2008, so an application that ignores that gives it no logger.
        services.TryAddSingleton<IOptions<TOptions>>(sp => new TenantFreeOptions<TOptions>(
            sp.GetRequiredService<IOptionsFactory<TOptions>>(),
            sp.GetRequiredService<ICurrentTenantId>(),
            TenantryWarnings.IsIgnored(sp, TenantryWarnings.OrdinaryOptionsReadAsTenant)
                ? NullLogger.Instance
                : sp.GetService<ILoggerFactory>()?.CreateLogger(TenantOptionsLog.Category) ?? NullLogger.Instance));
        services.TryAddScoped<IOptionsSnapshot<TOptions>>(sp => new TenantOptionsManager<TOptions>(
            sp.GetRequiredService<IOptionsFactory<TOptions>>(), sp.GetRequiredService<TenantOptionsCache<TOptions>>()));

        // The tenant's steps run after every ordinary Configure and before every PostConfigure, whatever the order
        // they were added in.
        services.TryAddTransient<IOptionsFactory<TOptions>>(sp => new TenantOptionsFactory<TOptions>(
            sp.GetServices<IConfigureOptions<TOptions>>(),
            sp.GetServices<ITenantOptionsStep<TOptions>>(),
            sp.GetServices<IPostConfigureOptions<TOptions>>(),
            sp.GetServices<IValidateOptions<TOptions>>()));

        services.AddSingleton<ITenantOptionsStep<TOptions>>(sp => new TenantConfigureOptions<TOptions, TKey>(
            sp.GetRequiredService<ITenantContext<TKey>>(), sp.GetRequiredService<IServiceScopeFactory>(), name, configure, withServices));

        return this;
    }
}
