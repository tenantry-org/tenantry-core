using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tenantry.Options.Internal;
using Tenantry.Tests.Shared;

namespace Tenantry.Options.Tests;

/// <summary>
/// <c>ConfigurePerTenant</c>: the snapshot and the monitor give the current tenant's value, built once per tenant from the
/// ordinary configuration and the tenant, until the tenant is invalidated or the configuration changes.
/// </summary>
public sealed class PerTenantOptionsTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private int _built;

    [Fact]
    public void TheSnapshotAndTheMonitor_GiveTheCurrentTenantsValue_AndTheOrdinaryOneWithoutATenant()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        foreach (var (tenant, colour) in new[] { (Acme, "red"), (Globex, "blue"), ((TenantDescriptor<string>?)null, "grey") })
        {
            using var _ = tenant is null ? null : MakeCurrent(provider, tenant);

            scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BrandingOptions>>().Value.Colour.Should().Be(colour);
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be(colour);
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Name.Should().Be("default", "the ordinary configuration applies first");
        }
    }

    [Fact]
    public void IOptions_GivesTheOrdinaryValue_WhicheverTenantIsCurrent()
    {
        using var provider = Build();

        foreach (var tenant in new[] { Acme, Globex })
        {
            using (MakeCurrent(provider, tenant))
                provider.GetRequiredService<IOptions<BrandingOptions>>().Value.Should().Match<BrandingOptions>(o => o.Colour == "grey" && o.Name == "default");
        }

        _built.Should().Be(0, "no tenant's step runs for IOptions");
    }

    [Fact]
    public void ReadingIOptionsAsATenant_LogsAWarningOnce_SinceTheCodeLikelyExpectsTheTenantsValue()
    {
        RecordingLoggers loggers = new();
        using var provider = Build(services => services.AddSingleton<ILoggerFactory>(loggers));
        var options = provider.GetRequiredService<IOptions<BrandingOptions>>();

        _ = options.Value;
        loggers.Entries.Should().BeEmpty("without a tenant the ordinary value is what is meant");

        using (MakeCurrent(provider, Acme))
            _ = options.Value;

        using (MakeCurrent(provider, Globex))
            _ = options.Value;

        loggers.Entries.Should().ContainSingle().Which.Should().Match<(string Category, int EventId, string Message)>(e =>
            e.Category == "Tenantry.Options" && e.EventId == 3001 &&
            e.Message.Contains("IOptions<BrandingOptions> was read while tenant acme is current"));
    }

    [Fact]
    public void ASingletonThatReadsIOptionsInItsConstructor_DoesNotKeepTheTenantThatCreatedIt()
    {
        // The usual pattern, _settings = options.CurrentValue: whatever it keeps, it keeps for every tenant.
        using var provider = Build(services => services.AddSingleton<CapturedBranding>());

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<CapturedBranding>().Colour.Should().Be("grey");

        using (MakeCurrent(provider, Globex))
            provider.GetRequiredService<CapturedBranding>().Colour.Should().Be("grey");
    }

    [Fact]
    public void ASingletonHoldingTheMonitor_ReadsTheTenantOfTheCodeThatCallsIt()
    {
        using var provider = Build(services => services.AddSingleton<Branding>());
        var branding = provider.GetRequiredService<Branding>();

        using (MakeCurrent(provider, Acme))
            branding.Colour.Should().Be("red");

        using (MakeCurrent(provider, Globex))
            branding.Colour.Should().Be("blue");
    }

    [Fact]
    public void ASingletonHoldingTheSnapshot_FailsScopeValidation()
    {
        FluentActions.Invoking(() => Build(services => services.AddSingleton<SnapshotBranding>()))
            .Should().Throw<AggregateException>().WithMessage("*Cannot consume scoped service*IOptionsSnapshot*");
    }

    [Fact]
    public async Task ATenantsValue_IsBuiltOnce_UntilTheTenantIsInvalidated()
    {
        using var provider = Build();
        var options = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();
        var tenants = provider.GetRequiredService<ITenantInvalidator<string>>();

        ReadAs(provider, options, Acme, Globex, Acme, Globex);
        _built.Should().Be(2);

        await tenants.InvalidateAsync("acme", TestContext.Current.CancellationToken);
        ReadAs(provider, options, Acme, Globex);
        _built.Should().Be(3, "only acme's value is built again");

        await tenants.InvalidateAllAsync(TestContext.Current.CancellationToken);
        ReadAs(provider, options, Acme, Globex);
        _built.Should().Be(5);
    }

    [Fact]
    public void TheTenantsStep_RunsAfterEveryOrdinaryConfigure_WhateverTheOrderTheyWereAddedIn()
    {
        using var provider = Build(services => services.Configure<BrandingOptions>(o => o.Colour = "green"));

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be("red");

        provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be("green", "without a tenant");
    }

    [Fact]
    public void SeveralSteps_ApplyInTheOrderAdded_ToOneCache()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant(perTenant => perTenant
                .Configure<BrandingOptions>((o, t) => o.Colour = "red")
                .Configure<BrandingOptions>((o, t) => o.Colour += $"-{t.Name}")));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be("red-Acme");

        services.Count(d => d.ServiceType == typeof(IOptionsMonitorCache<BrandingOptions>)).Should().Be(1);
    }

    [Fact]
    public void TheBuilder_KeepsTheKeyType_ForTheStepsAndTheMethodsAfterIt()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t) => o.Name = t.TenantId.ToUpperInvariant()))
            .CacheTenants());
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Name.Should().Be("ACME");
    }

    [Fact]
    public void AStepWithServices_GetsAScopeOfItsOwn_WhichIsDisposedAfterIt()
    {
        ServiceCollection services = new();
        services.AddScoped<ColourSource>();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t, sp) => o.Colour = sp.GetRequiredService<ColourSource>().For(t))));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>().CurrentValue.Colour.Should().Be("from-store-Acme");

        ColourSource.Disposed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AChangeToTheBoundConfiguration_ClearsEveryTenantsValue()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("Branding:Name", "first")]).Build();
        using var provider = Build(services => services.Configure<BrandingOptions>(configuration.GetSection("Branding")));
        var options = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
            options.CurrentValue.Name.Should().Be("first");

        configuration["Branding:Name"] = "second";
        configuration.Reload();

        using (MakeCurrent(provider, Acme))
            options.CurrentValue.Should().Match<BrandingOptions>(o => o.Name == "second" && o.Colour == "red");
    }

    [Fact]
    public void OnlyTheTypesConfiguredPerTenant_AreAffected()
    {
        using var provider = Build(services => services.Configure<OtherOptions>(o => o.Value = "plain"));

        provider.GetRequiredService<IOptions<OtherOptions>>().GetType().Assembly
            .Should().NotBeSameAs(typeof(TenantryOptionsTenantBuilderExtensions).Assembly);

        using (MakeCurrent(provider, Acme))
            provider.GetRequiredService<IOptions<OtherOptions>>().Value.Value.Should().Be("plain");
    }

    [Fact]
    public void ANamedValue_IsKeptPerTenant_AndADefaultNameStepDoesNotApplyToIt()
    {
        using var provider = Build(services => services.Configure<BrandingOptions>("print", o => o.Name = "print"));
        using var scope = provider.CreateScope();

        using (MakeCurrent(provider, Acme))
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
            services.AddTenantry<string>(tenant => tenant.ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>("print", (o, t) => o.Colour = $"ink-{t.Name}")));
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
        {
            monitor.Get("print").Should().Match<BrandingOptions>(o => o.Name == "print" && o.Colour == "ink-Acme");
            monitor.Get("web").Colour.Should().Be("grey");
            monitor.CurrentValue.Colour.Should().Be("red", "the default name keeps its own step");
        }

        using (MakeCurrent(provider, Globex))
            monitor.Get("print").Colour.Should().Be("ink-Globex");

        monitor.Get("print").Colour.Should().Be("grey", "without a tenant");
    }

    [Fact]
    public void AStepForEveryName_AppliesToTheDefaultAndEveryNamedValue()
    {
        using var provider = Build(services => services.AddTenantry<string>(tenant =>
            tenant.ConfigurePerTenant(perTenant => perTenant.ConfigureAll<BrandingOptions>((o, t, sp) => o.Name = $"all-{t.Name}"))));
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
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
            services.AddTenantry<string>(tenant => tenant.ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>("print", (o, t) => o.Colour = "ink")));
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
        {
            monitor.CurrentValue.Name.Should().Be("built-from-red");
            monitor.Get("print").Name.Should().Be("built-from-ink");
        }
    }

    [Fact]
    public void Validation_RunsOnEachTenantsValue_WhenItIsBuilt()
    {
        using var provider = Build(services => services.AddOptions<BrandingOptions>().Validate(o => o.Colour != "blue", "No blue."));
        var options = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
            options.CurrentValue.Colour.Should().Be("red");

        using (MakeCurrent(provider, Globex))
            FluentActions.Invoking(() => options.CurrentValue).Should().Throw<OptionsValidationException>().WithMessage("No blue.");
    }

    [Fact]
    public void AStepThatThrows_IsRunAgainOnTheNextRead()
    {
        var calls = 0;
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme])
            .ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t) =>
            {
                if (++calls == 1)
                    throw new TimeoutException("The database did not answer.");

                o.Colour = "red";
            })));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
        {
            FluentActions.Invoking(() => monitor.CurrentValue).Should().Throw<TimeoutException>();
            monitor.CurrentValue.Colour.Should().Be("red");

            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BrandingOptions>>().Value.Colour.Should().Be("red");
        }

        calls.Should().Be(2);
    }

    [Fact]
    public async Task AReadAfterAnInvalidation_BuildsFromTheStoresTenant_NotTheCopyAnEarlierRequestCarries()
    {
        // A request resolved before the tenant changed reads the options after the invalidation.
        var store = new ChangingStore(Acme);
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseStore(_ => store)
            .ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t) => o.Name = t.Name)));
        using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        using (MakeCurrent(provider, Acme))
        {
            monitor.CurrentValue.Name.Should().Be("Acme");

            store.Tenant = new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Renamed" };
            await provider.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);

            monitor.CurrentValue.Name.Should().Be("Acme Renamed");
        }

        using (MakeCurrent(provider, store.Tenant))
            monitor.CurrentValue.Name.Should().Be("Acme Renamed");
    }

    [Fact]
    public async Task ACopyThatDiffersFromTheStores_GetsTheStoresValue_BeforeAndAfterAnInvalidation()
    {
        var store = new ChangingStore(Acme);
        using var provider = BuildNamedAfterTheTenant(tenant => tenant.UseStore(_ => store));
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();
        TenantDescriptor<string> forged = new() { TenantId = "acme", Name = "Forged" };

        ReadNames(provider, monitor, forged, Acme).Should().Equal("Acme", "Acme");

        await provider.GetRequiredService<ITenantInvalidator<string>>()
            .InvalidateAsync("acme", TestContext.Current.CancellationToken);

        ReadNames(provider, monitor, forged, Acme).Should().Equal("Acme", "Acme");
    }

    [Fact]
    public void IdsTheStoreDoesNotHold_AreBuiltFromTheCallersCopy_AndNotKept()
    {
        using var provider = BuildNamedAfterTheTenant(tenant => tenant.UseInMemoryStore([Acme]));
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();
        var unknown = Enumerable.Range(0, 1000)
            .Select(i => new TenantDescriptor<string> { TenantId = $"x{i}", Name = $"X{i}" })
            .ToArray();

        ReadNames(provider, monitor, unknown).Should().Equal(unknown.Select(t => t.Name));
        ReadNames(provider, monitor, new TenantDescriptor<string> { TenantId = "x0", Name = "X0 again" })
            .Should().Equal("X0 again");

        _built.Should().Be(1001);
        KeptValues(provider).Should().Be(0);
    }

    [Fact]
    public void WithoutAStore_TheCallersCopyIsBuiltOnceAndKept()
    {
        using var provider = BuildNamedAfterTheTenant(_ => { });
        var monitor = provider.GetRequiredService<IOptionsMonitor<BrandingOptions>>();

        ReadNames(provider, monitor, Acme, new TenantDescriptor<string> { TenantId = "acme", Name = "Other copy" })
            .Should().Equal("Acme", "Acme");

        _built.Should().Be(1);
        KeptValues(provider).Should().Be(1);
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
            .ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t) =>
            {
                _built++;
                o.Colour = t.Name == "Acme" ? "red" : "blue";
            })));
        configure?.Invoke(services);
        return services;
    }

    private ServiceProvider Build(Action<IServiceCollection>? configure = null) =>
        Services(configure).BuildServiceProvider(Conformance.ProviderOptions);

    private static IDisposable MakeCurrent(IServiceProvider provider, ITenantDescriptor<string> tenant) =>
        provider.GetRequiredService<ITenantContextSetter<string>>().MakeCurrent(tenant);

    // A provider whose step names the options after the tenant it is given, and counts its runs.
    private ServiceProvider BuildNamedAfterTheTenant(Action<ITenantBuilder<string>> store)
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            store(tenant);
            tenant.ConfigurePerTenant(perTenant => perTenant.Configure<BrandingOptions>((o, t) =>
            {
                _built++;
                o.Name = t.Name;
            }));
        });
        return services.BuildServiceProvider(Conformance.ProviderOptions);
    }

    private static int KeptValues(IServiceProvider provider) =>
        provider.GetRequiredService<TenantOptionsCache<BrandingOptions>>().Count;

    private static List<string> ReadNames(
        IServiceProvider provider, IOptionsMonitor<BrandingOptions> options, params ITenantDescriptor<string>[] tenants)
    {
        List<string> names = [];

        foreach (var tenant in tenants)
        {
            using (MakeCurrent(provider, tenant))
                names.Add(options.CurrentValue.Name);
        }

        return names;
    }

    private static void ReadAs(IServiceProvider provider, IOptionsMonitor<BrandingOptions> options, params ITenantDescriptor<string>[] tenants)
    {
        foreach (var tenant in tenants)
        {
            using (MakeCurrent(provider, tenant))
                _ = options.CurrentValue;
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

    private sealed class Branding(IOptionsMonitor<BrandingOptions> monitor)
    {
        public string Colour => monitor.CurrentValue.Colour;
    }

    private sealed class CapturedBranding(IOptions<BrandingOptions> settings)
    {
        public string Colour { get; } = settings.Value.Colour;
    }

    private sealed class SnapshotBranding(IOptionsSnapshot<BrandingOptions> snapshot)
    {
        public string Colour => snapshot.Value.Colour;
    }

    private sealed class RecordingLoggers : ILoggerFactory
    {
        public List<(string Category, int EventId, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private sealed class Logger(RecordingLoggers loggers, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                loggers.Entries.Add((category, eventId.Id, formatter(state, exception)));
        }
    }

    private sealed class ChangingStore(ITenantDescriptor<string> tenant) : ITenantStore<string>
    {
        public ITenantDescriptor<string> Tenant { get; set; } = tenant;

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITenantDescriptor<string>?>(tenantId == Tenant.TenantId ? Tenant : null);

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([Tenant]);
    }

    private sealed class ColourSource : IDisposable
    {
        public static int Disposed;

        public string For(ITenantDescriptor tenant) => $"from-store-{tenant.Name}";

        public void Dispose() => Interlocked.Increment(ref Disposed);
    }
}
