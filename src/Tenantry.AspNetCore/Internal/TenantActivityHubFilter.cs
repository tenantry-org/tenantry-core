using Microsoft.AspNetCore.SignalR;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Refuses a hub method call whose connection's tenant no longer exists or is no longer active. Added by
/// <c>hubOptions.AddTenantry()</c>. Blazor Server's own hub is not checked: a refused call there would leave the
/// circuit open but not responding, and <c>AddInteractiveServerComponents().AddTenantry()</c> checks circuits instead.
/// </summary>
internal sealed class TenantActivityHubFilter : IHubFilter
{
    // Blazor Server's hub, which is internal to Microsoft.AspNetCore.Components.Server.
    private const string BlazorServerHub = "Microsoft.AspNetCore.Components.Server.ComponentHub";

    public static TenantActivityHubFilter Instance { get; } = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocationContext.Hub.GetType().FullName == BlazorServerHub)
        {
            return await next(invocationContext).ConfigureAwait(false);
        }

        await TenantActivityCheck.ThrowIfInactiveAsync(
                invocationContext.ServiceProvider,
                "hubOptions.AddTenantry()",
                invocationContext.Context.ConnectionAborted)
            .ConfigureAwait(false);

        return await next(invocationContext).ConfigureAwait(false);
    }
}
