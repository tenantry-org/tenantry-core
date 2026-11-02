using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Refuses a Blazor Server circuit's inbound activity (a browser event, a JavaScript interop call, a navigation) when
/// the circuit's tenant is not the current one, no longer exists or is no longer active, which ends the circuit. Added
/// by <c>AddInteractiveServerComponents().AddTenantry()</c>, in each circuit's scope.
/// </summary>
internal sealed class TenantActivityCircuitHandler(IServiceProvider services) : CircuitHandler
{
    private const string Registration = "AddInteractiveServerComponents().AddTenantry()";

    // The tenant of the request that opened the circuit. An activity runs in the flow of the circuit's connection, and
    // a reconnect brings a new one, whose request may have resolved another tenant or none.
    private bool _opened;
    private ITenantDescriptor? _tenant;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tenant = TenantActivityCheck.CurrentTenant(services, Registration);
        _opened = true;
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            // Blazor opens a circuit before its first activity. An activity that came first would still be on the
            // opening connection, so it is checked against the current tenant alone rather than refused.
            var check = _opened
                ? TenantActivityCheck.ThrowIfNotCurrentOrInactiveAsync(
                    services,
                    Registration,
                    _tenant,
                    CancellationToken.None)
                : TenantActivityCheck.ThrowIfInactiveAsync(services, Registration, CancellationToken.None);

            // Some activity starts on the circuit's dispatcher, and must continue there.
            await check.ConfigureAwait(true);
            await next(context).ConfigureAwait(true);
        };
}
