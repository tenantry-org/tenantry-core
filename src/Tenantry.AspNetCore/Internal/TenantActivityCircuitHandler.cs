using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Refuses a Blazor Server circuit's inbound activity (a browser event, a JavaScript interop call, a navigation) when
/// the circuit's tenant no longer exists or is no longer active, which ends the circuit. Added by
/// <c>AddInteractiveServerComponents().AddTenantry()</c>, in each circuit's scope.
/// </summary>
internal sealed class TenantActivityCircuitHandler(IServiceProvider services) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            // Some activity starts on the circuit's dispatcher, and must continue there.
            await TenantActivityCheck.ThrowIfInactiveAsync(
                    services,
                    "AddInteractiveServerComponents().AddTenantry()",
                    CancellationToken.None)
                .ConfigureAwait(true);

            await next(context).ConfigureAwait(true);
        };
}
