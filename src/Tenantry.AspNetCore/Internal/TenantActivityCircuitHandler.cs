using Microsoft.AspNetCore.Components.Server.Circuits;
#if NET10_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
#endif

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

#if NET10_0_OR_GREATER
    // Where a paused circuit's state records its tenant's id, so the circuit that resumes it can be checked.
    internal const string StateKey = "Tenantry.CircuitTenant";

    // Why a resumed circuit's activity is refused: it resumed the state of another tenant's circuit.
    private InvalidOperationException? _refusal;
#endif

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tenant = TenantActivityCheck.CurrentTenant(services, Registration);
        _opened = true;
#if NET10_0_OR_GREATER
        if (services.GetService<PersistentComponentState>() is { } state)
        {
            CheckResumedState(state);
        }
#endif
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
#if NET10_0_OR_GREATER
            if (_refusal is not null)
            {
                throw _refusal;
            }
#endif

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

#if NET10_0_OR_GREATER
    // .NET 10 can pause a circuit (Blazor.pauseCircuit(), or when it evicts a disconnected one) and resume its state in
    // a new circuit, whose tenant is the resuming request's. Blazor restores that state before it opens the circuit,
    // and adds the circuit's components as its first activity, so a circuit that resumes another tenant's state, or
    // state that does not say its tenant, is refused at that activity and ends before it renders anything.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The state is a string, and Blazor Server supports neither trimming nor Native AOT.")]
    private void CheckResumedState(PersistentComponentState state)
    {
        var tenantId = TenantActivityCheck.FormatId(services, Registration, _tenant);

        // Runs now only when the state was restored from a paused circuit, not from prerendering.
        state.RegisterOnRestoring(
            () => _refusal = Refusal(state.TryTakeFromJson<string>(StateKey, out var paused), paused, tenantId),
            new RestoreOptions { RestoreBehavior = RestoreBehavior.SkipInitialValue });

        state.RegisterOnPersisting(
            () =>
            {
                state.PersistAsJson(StateKey, tenantId);
                return Task.CompletedTask;
            },
            RenderMode.InteractiveServer);
    }

    private static InvalidOperationException? Refusal(bool recorded, string? paused, string? resumed) =>
        (recorded, paused, resumed) switch
        {
            (false, _, _) => new InvalidOperationException(
                "This Blazor Server circuit resumes the state of a paused circuit that does not say its tenant, so " +
                "the circuit ends. Only a circuit paused with AddInteractiveServerComponents().AddTenantry() records " +
                "its tenant."),
            _ when paused == resumed => null,
            (_, not null, null) => new TenantNotResolvedException(
                $"This Blazor Server circuit resumes the state of a circuit of tenant '{paused}', but the request " +
                "that resumed it identifies no tenant, so the circuit ends. The request resolved no tenant, as it " +
                "does for a tenant that is no longer active."),
            _ => new InvalidOperationException(
                (paused is null
                    ? "This Blazor Server circuit resumes the state of a circuit without a tenant"
                    : $"This Blazor Server circuit resumes the state of a circuit of tenant '{paused}'") +
                $", but the request that resumed it identifies tenant '{resumed}', so the circuit ends. A circuit " +
                "keeps the tenant it opened with, through a pause too."),
        };
#endif
}
