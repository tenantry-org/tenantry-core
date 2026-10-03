using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tenantry;
using Tenantry.Options.Internal;

// Extensions on the Tenantry builder live in the DI namespace, so registration code needs no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Options with values per tenant.
/// </summary>
public static class TenantryOptionsTenantBuilderExtensions
{
    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant: <c>IOptionsSnapshot&lt;TOptions&gt;</c> and
    /// <c>IOptionsMonitor&lt;TOptions&gt;</c> give the current tenant's value, built from the ordinary configuration
    /// (every <c>Configure</c>), then <paramref name="configure"/> with the tenant. Without a tenant they give the
    /// ordinary value. <c>IOptions&lt;TOptions&gt;</c> always gives the ordinary value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IOptions&lt;TOptions&gt;</c> is not per tenant because its value is read once and kept: a singleton that reads
    /// <c>options.Value</c> in its constructor would keep the first tenant's settings and use them for every tenant.
    /// Read <c>IOptionsSnapshot&lt;TOptions&gt;</c>, which is scoped, in request code, and hold
    /// <c>IOptionsMonitor&lt;TOptions&gt;</c> in a singleton and read <c>CurrentValue</c> each time. Reading
    /// <c>CurrentValue</c> once in a constructor keeps one tenant's value, as reading <c>Value</c> would.
    /// </para>
    /// <para>
    /// Each tenant's value is built on first use and cached; <see cref="ITenantStoreCache{TKey}.Invalidate"/> clears it,
    /// so changing a tenant's settings is followed by invalidating the tenant. A change to the configuration the options
    /// are bound to clears every tenant's value. Validation (<c>Validate</c>, <c>IValidateOptions</c>) runs on each
    /// tenant's value when it is built, and <c>ValidateOnStart</c> validates the ordinary one.
    /// </para>
    /// <para>
    /// It applies to the default-named options; for named options, such as an authentication scheme's, use the overload
    /// that takes a name, or <c>ConfigureAllPerTenant</c>. The tenant's steps run after every <c>Configure</c> and before
    /// every <c>PostConfigure</c>. It has a type parameter of its own, so it returns the non-generic builder: call it
    /// after the methods that need the tenant key type.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant (read your tenant type with <c>As&lt;T&gt;()</c>).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.Configure&lt;BrandingOptions&gt;(builder.Configuration.GetSection("Branding"));   // the defaults
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .ConfigurePerTenant&lt;BrandingOptions&gt;((options, t) =&gt; options.Colour = t.As&lt;AppTenant&gt;().BrandColour));
    /// </code>
    /// </example>
    public static ITenantBuilder ConfigurePerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(this ITenantBuilder builder, Action<TOptions, ITenantDescriptor> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(DefaultName, (options, tenant, _) => configure(options, tenant), WithServices: false));
        return builder;
    }

    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant, as
    /// <see cref="ConfigurePerTenant{TOptions}(ITenantBuilder, Action{TOptions, ITenantDescriptor})"/> does, for the
    /// options named <paramref name="name"/>: an authentication scheme's, for example, which its handler reads with
    /// <c>IOptionsMonitor&lt;TOptions&gt;.Get(scheme)</c>.
    /// </summary>
    /// <remarks>
    /// The tenant's steps run after every <c>Configure</c> of the name and before every <c>PostConfigure</c>, so an
    /// authentication handler's post-configuration (which builds JWT bearer's and OpenID Connect's metadata manager from
    /// <c>Authority</c>) sees the tenant's values. For authentication, the tenant must be current before the
    /// authentication middleware runs: see <c>app.UseTenantResolution()</c>.
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="name">The options' name, such as an authentication scheme's.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// tenant.ConfigurePerTenant&lt;JwtBearerOptions&gt;(JwtBearerDefaults.AuthenticationScheme, (options, t) =&gt;
    ///     options.Authority = t.As&lt;AppTenant&gt;().Authority);
    /// </code>
    /// </example>
    public static ITenantBuilder ConfigurePerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this ITenantBuilder builder, string name, Action<TOptions, ITenantDescriptor> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(name, (options, tenant, _) => configure(options, tenant), WithServices: false));
        return builder;
    }

    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant, as
    /// <see cref="ConfigurePerTenant{TOptions}(ITenantBuilder, string, Action{TOptions, ITenantDescriptor})"/> does,
    /// with the services of a scope created for the step.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="name">The options' name, such as an authentication scheme's.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder ConfigurePerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this ITenantBuilder builder, string name, Action<TOptions, ITenantDescriptor, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(name, (options, tenant, services) => configure(options, tenant, services!), WithServices: true));
        return builder;
    }

    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant, as
    /// <see cref="ConfigurePerTenant{TOptions}(ITenantBuilder, Action{TOptions, ITenantDescriptor})"/> does, for the
    /// default options and every named instance: every authentication scheme of the type, for example.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder ConfigureAllPerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this ITenantBuilder builder, Action<TOptions, ITenantDescriptor> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(null, (options, tenant, _) => configure(options, tenant), WithServices: false));
        return builder;
    }

    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant, as
    /// <see cref="ConfigureAllPerTenant{TOptions}(ITenantBuilder, Action{TOptions, ITenantDescriptor})"/> does, with the
    /// services of a scope created for the step.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ITenantBuilder ConfigureAllPerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this ITenantBuilder builder, Action<TOptions, ITenantDescriptor, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(null, (options, tenant, services) => configure(options, tenant, services!), WithServices: true));
        return builder;
    }

    /// <summary>
    /// Configures <typeparamref name="TOptions"/> per tenant, as
    /// <see cref="ConfigurePerTenant{TOptions}(ITenantBuilder, Action{TOptions, ITenantDescriptor})"/> does, with the
    /// services of a scope created for the step: for settings read from a database. The scope is disposed after the
    /// step, and the tenant is current while it runs.
    /// </summary>
    /// <remarks>
    /// Options have no asynchronous configuration, so the step runs synchronously, once per tenant until the tenant is
    /// invalidated.
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the tenant's values, with the tenant and the scope's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// tenant.ConfigurePerTenant&lt;BrandingOptions&gt;((options, t, services) =&gt;
    ///     options.Colour = services.GetRequiredService&lt;AppDbContext&gt;().Settings.Single(s =&gt; s.Key == "colour").Value);
    /// </code>
    /// </example>
    public static ITenantBuilder ConfigurePerTenant<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this ITenantBuilder builder, Action<TOptions, ITenantDescriptor, IServiceProvider> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Add(new PerTenantOptions<TOptions>(DefaultName, (options, tenant, services) => configure(options, tenant, services!), WithServices: true));
        return builder;
    }

    private const string DefaultName = "";

    // Name null: every name.
    private sealed record PerTenantOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(string? Name, Action<TOptions, ITenantDescriptor, IServiceProvider?> Configure, bool WithServices)
        : ITenantRegistration
        where TOptions : class
    {
        public void Apply<TKey>(ITenantBuilder<TKey> tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            var services = tenant.Services;
            services.AddOptions();

            services.TryAddSingleton<ICurrentTenantId>(sp => new CurrentTenantId<TKey>(
                sp.GetRequiredService<ITenantContextSetter<TKey>>(), sp.GetRequiredService<TenantOptionsCaches>(), sp));
            services.TryAddSingleton<TenantOptionsCaches>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<TKey>, TenantOptionsInvalidation<TKey>>());

            // The options type's own cache and readers, once; IOptionsMonitor<TOptions> reads the cache too.
            services.TryAddSingleton(sp => new TenantOptionsCache<TOptions>(
                sp.GetRequiredService<ICurrentTenantId>(),
                sp.GetRequiredService<TenantOptionsCaches>(),
                sp.GetServices<IOptionsChangeTokenSource<TOptions>>()));
            services.TryAddSingleton<IOptionsMonitorCache<TOptions>>(sp => sp.GetRequiredService<TenantOptionsCache<TOptions>>());
            // IOptions<TOptions> stays the ordinary value: a singleton that reads it once must not keep a tenant's.
            services.TryAddSingleton<IOptions<TOptions>>(sp => new TenantFreeOptions<TOptions>(
                sp.GetRequiredService<IOptionsFactory<TOptions>>(), sp.GetRequiredService<ICurrentTenantId>()));
            services.TryAddScoped<IOptionsSnapshot<TOptions>>(sp => new TenantOptionsManager<TOptions>(
                sp.GetRequiredService<IOptionsFactory<TOptions>>(), sp.GetRequiredService<TenantOptionsCache<TOptions>>()));

            // The tenant's steps run after every ordinary Configure and before every PostConfigure, whatever the order
            // they were added in.
            services.TryAddTransient<IOptionsFactory<TOptions>>(sp => new TenantOptionsFactory<TOptions>(
                sp.GetServices<IConfigureOptions<TOptions>>(),
                sp.GetServices<ITenantOptionsStep<TOptions>>(),
                sp.GetServices<IPostConfigureOptions<TOptions>>(),
                sp.GetServices<IValidateOptions<TOptions>>()));

            var name = Name;
            var configure = Configure;
            var withServices = WithServices;
            services.AddSingleton<ITenantOptionsStep<TOptions>>(sp => new TenantConfigureOptions<TOptions, TKey>(
                sp.GetRequiredService<ITenantContext<TKey>>(), sp.GetRequiredService<IServiceScopeFactory>(), name, configure, withServices));
        }
    }
}
