using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// The circuit handler <c>AddInteractiveServerComponents().AddTenantry()</c> adds, run as Blazor Server runs it around
/// a circuit's inbound activity, with the circuit's tenant current.
/// </summary>
public sealed class BlazorServerTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    private readonly SignalRTests.Tenants _tenants = new();
    private int _runs;

    [Fact]
    public async Task AnActiveTenantsActivityRuns()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();

        using (Current(provider))
        {
            await Activity(circuit)(null!);
        }

        _runs.Should().Be(1);
    }

    [Fact]
    public async Task ASuspendedTenantsActivity_IsRefused()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Suspend("acme");

        using (Current(provider))
        {
            await Activity(circuit).Awaiting(run => run(null!)).Should().ThrowAsync<TenantInactiveException>();
        }

        _runs.Should().Be(0);
    }

    [Fact]
    public async Task ADeletedTenantsActivity_IsRefused()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Delete("acme");

        using (Current(provider))
        {
            await Activity(circuit).Awaiting(run => run(null!)).Should().ThrowAsync<TenantNotFoundException>();
        }

        _runs.Should().Be(0);
    }

    [Fact]
    public async Task ACircuitWithoutATenant_IsNotChecked()
    {
        await using var provider = Build();
        using var circuit = provider.CreateScope();
        _tenants.Suspend("acme");

        await Activity(circuit)(null!);

        _runs.Should().Be(1);
    }

    [Fact]
    public void AddingItTwice_AddsOneHandler()
    {
        ServiceCollection services = new();
        services.AddServerSideBlazor().AddTenantry().AddTenantry();

        services.Count(descriptor => descriptor.ImplementationType == typeof(TenantActivityCircuitHandler)).Should().Be(1);
    }

    private ServiceProvider Build()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(tenant => tenant
            .UseStore(_ => _tenants)
            .ValidateTenantActivity(t => !t.As<SignalRTests.AppTenant>().Suspended));
        services.AddRazorComponents().AddInteractiveServerComponents().AddTenantry();
        return services.BuildServiceProvider();
    }

    private static IDisposable Current(IServiceProvider provider) =>
        provider.GetRequiredService<ITenantContextSetter<string>>().MakeCurrent(Acme);

    // The handler from the circuit's scope, around an activity that counts its runs.
    private Func<CircuitInboundActivityContext, Task> Activity(IServiceScope circuit) =>
        circuit.ServiceProvider.GetServices<CircuitHandler>().OfType<TenantActivityCircuitHandler>().Single()
            .CreateInboundActivityHandler(_ =>
            {
                _runs++;
                return Task.CompletedTask;
            });
}
