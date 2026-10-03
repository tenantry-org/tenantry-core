using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tenantry.Tests.Shared;

namespace Tenantry.Options.Tests;

/// <summary>
/// <c>ConfigurePerTenant</c>: the options readers give the current tenant's value, built once per tenant from the
/// ordinary configuration and the tenant, until the tenant is invalidated or the configuration changes.
/// </summary>
public sealed class PerTenantOptionsTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private int _built;

    [Fact]
    public void EveryReader_GivesTheCurrentTenantsValue_AndTheOrdinaryOneWithoutATenant()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        foreach (var (tenant, colour) in new[] { (Acme, "red"), (Globex, "blue"), ((TenantDescriptor<string>?)null, "grey") })
        {
            using var _ = tenant is null ? null : Use(provider, tenant);

            provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Colour.Should().Be(colour);
            scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BrandingOptions>>().Value.Colour.Should().Be(colour);
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be(colour);
            provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Name.Should().Be("default", "the ordinary configuration applies first");
        }
    }

    [Fact]
    public void ASingletonHoldingIOptions_ReadsTheTenantOfTheCodeThatCallsIt()
    {
        using var provider = Build(services => services.AddSingleton<Branding>());
        var branding = provider.GetRequiredService<Branding>();

        using (Use(provider, Acme))
            branding.Colour.Should().Be("red");

        using (Use(provider, Globex))
            branding.Colour.Should().Be("blue");
    }

    [Fact]
    public void ATenantsValue_IsBuiltOnce_UntilTheTenantIsInvalidated()
    {
        using var provider = Build();
        var options = provider.GetRequiredService<IOptions<BrandingOptions>>();
        var tenants = provider.GetRequiredService<ITenantStoreCache<string>>();

        ReadAs(provider, options, Acme, Globex, Acme, Globex);
        _built.Should().Be(2);

        tenants.Invalidate("acme");
        ReadAs(provider, options, Acme, Globex);
        _built.Should().Be(3, "only acme's value is built again");

        tenants.InvalidateAll();
        ReadAs(provider, options, Acme, Globex);
        _built.Should().Be(5);
    }

    [Fact]
    public void TheTenantsStep_RunsAfterEveryOrdinaryConfigure_WhateverTheOrderTheyWereAddedIn()
    {
        using var provider = Build(services => services.Configure<BrandingOptions>(o => o.Colour = "green"));

        using (Use(provider, Acme))
            provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Colour.Should().Be("red");

        provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Colour.Should().Be("green", "without a tenant");
    }

    [Fact]
    public void SeveralSteps_ApplyInTheOrderAdded_ToOneCache()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant<BrandingOptions>((o, t) => o.Colour = "red")
            .ConfigurePerTenant<BrandingOptions>((o, t) => o.Colour += $"-{t.Name}"));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);

        using (Use(provider, Acme))
            provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Colour.Should().Be("red-Acme");

        services.Count(d => d.ServiceType == typeof(IOptionsMonitorCache<BrandingOptions>)).Should().Be(1);
    }

    [Fact]
    public void AStepWithServices_GetsAScopeOfItsOwn_WhichIsDisposedAfterIt()
    {
        ServiceCollection services = new();
        services.AddScoped<ColourSource>();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant<BrandingOptions>((o, t, sp) => o.Colour = sp.GetRequiredService<ColourSource>().For(t)));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);

        using (Use(provider, Acme))
            provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Colour.Should().Be("from-store-Acme");

        ColourSource.Disposed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AChangeToTheBoundConfiguration_ClearsEveryTenantsValue()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("Branding:Name", "first")]).Build();
        using var provider = Build(services => services.Configure<BrandingOptions>(configuration.GetSection("Branding")));
        var options = provider.GetRequiredService<IOptions<BrandingOptions>>();

        using (Use(provider, Acme))
            options.Value.Name.Should().Be("first");

        configuration["Branding:Name"] = "second";
        configuration.Reload();

        using (Use(provider, Acme))
            options.Value.Should().Match<BrandingOptions>(o => o.Name == "second" && o.Colour == "red");
    }

    [Fact]
    public void OnlyTheTypesConfiguredPerTenant_AreAffected()
    {
        using var provider = Build(services => services.Configure<OtherOptions>(o => o.Value = "plain"));

        provider.GetRequiredService<IOptions<OtherOptions>>().GetType().Assembly
            .Should().NotBeSameAs(typeof(TenantryOptionsTenantBuilderExtensions).Assembly);

        using (Use(provider, Acme))
            provider.GetRequiredService<IOptions<OtherOptions>>().Value.Value.Should().Be("plain");
    }

    [Fact]
    public void ANamedValue_IsKeptPerTenant_AndADefaultNameStepDoesNotApplyToIt()
    {
        using var provider = Build(services => services.Configure<BrandingOptions>("print", o => o.Name = "print"));
        using var scope = provider.CreateScope();

        using (Use(provider, Acme))
            scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BrandingOptions>>().Get("print")
                .Should().Match<BrandingOptions>(o => o.Name == "print" && o.Colour == "grey");
    }

    [Fact]
    public void ANamedStep_AppliesToItsNameOnly()
    {
        using var provider = Build(services =>
        {
            services.Configure<BrandingOptions>("print", o => o.Name = "print");
            services.Configure<BrandingOptions>("web", o => o.Name = "web");
            services.AddTenantry<string>(tenant => tenant.ConfigurePerTenant<BrandingOptions>("print", (o, t) => o.Colour = $"ink-{t.Name}"));
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (Use(provider, Acme))
        {
            monitor.Get("print").Should().Match<BrandingOptions>(o => o.Name == "print" && o.Colour == "ink-Acme");
            monitor.Get("web").Colour.Should().Be("grey");
            monitor.CurrentValue.Colour.Should().Be("red", "the default name keeps its own step");
        }

        using (Use(provider, Globex))
            monitor.Get("print").Colour.Should().Be("ink-Globex");

        monitor.Get("print").Colour.Should().Be("grey", "without a tenant");
    }

    [Fact]
    public void AStepForEveryName_AppliesToTheDefaultAndEveryNamedValue()
    {
        using var provider = Build(services => services.AddTenantry<string>(tenant =>
            tenant.ConfigureAllPerTenant<BrandingOptions>((o, t, sp) => o.Name = $"all-{t.Name}")));
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (Use(provider, Acme))
        {
            monitor.Get("anything").Name.Should().Be("all-Acme");
            monitor.CurrentValue.Should().Match<BrandingOptions>(o => o.Name == "all-Acme" && o.Colour == "red");
        }
    }

    [Fact]
    public void TheTenantsSteps_RunBeforeEveryPostConfigure_WhateverTheOrderTheyWereAddedIn()
    {
        // As an authentication handler's post-configuration does: it builds what it needs from the settings it sees.
        using var provider = Build(services =>
        {
            services.PostConfigureAll<BrandingOptions>(o => o.Name = $"built-from-{o.Colour}");
            services.AddTenantry<string>(tenant => tenant.ConfigurePerTenant<BrandingOptions>("print", (o, t) => o.Colour = "ink"));
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (Use(provider, Acme))
        {
            monitor.CurrentValue.Name.Should().Be("built-from-red");
            monitor.Get("print").Name.Should().Be("built-from-ink");
        }
    }

    [Fact]
    public void Validation_RunsOnEachTenantsValue_WhenItIsBuilt()
    {
        using var provider = Build(services => services.AddOptions<BrandingOptions>().Validate(o => o.Colour != "blue", "No blue."));
        var options = provider.GetRequiredService<IOptions<BrandingOptions>>();

        using (Use(provider, Acme))
            options.Value.Colour.Should().Be("red");

        using (Use(provider, Globex))
            FluentActions.Invoking(() => options.Value).Should().Throw<OptionsValidationException>().WithMessage("No blue.");
    }

    [Fact]
    public void EveryRegistration_Resolves_InAValidatedProvider()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Conformance.ResolveEveryTenantryService(Services(), scope.ServiceProvider);
    }

    private ServiceCollection Services(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        services.Configure<BrandingOptions>(o => o.Name = "default");
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme, Globex])
            .ConfigurePerTenant<BrandingOptions>((o, t) =>
            {
                _built++;
                o.Colour = t.Name == "Acme" ? "red" : "blue";
            }));
        configure?.Invoke(services);
        return services;
    }

    private ServiceProvider Build(Action<IServiceCollection>? configure = null) =>
        Services(configure).BuildServiceProvider(Conformance.ProviderOptions);

    private static IDisposable Use(IServiceProvider provider, ITenantDescriptor<string> tenant) =>
        provider.GetRequiredService<ITenantContextSetter<string>>().Use(tenant);

    private static void ReadAs(IServiceProvider provider, IOptions<BrandingOptions> options, params ITenantDescriptor<string>[] tenants)
    {
        foreach (var tenant in tenants)
        {
            using (Use(provider, tenant))
                _ = options.Value;
        }
    }

    public sealed class BrandingOptions
    {
        public string Colour { get; set; } = "grey";

        public string Name { get; set; } = "";
    }

    public sealed class OtherOptions
    {
        public string Value { get; set; } = "";
    }

    private sealed class Branding(IOptions<BrandingOptions> options)
    {
        public string Colour => options.Value.Colour;
    }

    private sealed class ColourSource : IDisposable
    {
        public static int Disposed;

        public string For(ITenantDescriptor tenant) => $"from-store-{tenant.Name}";

        public void Dispose() => Interlocked.Increment(ref Disposed);
    }
}
