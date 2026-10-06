using Microsoft.AspNetCore.SignalR;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Refuses a hub method call whose connection's tenant no longer exists or is no longer active. Added by
/// <c>hubOptions.AddTenantry()</c>.
/// </summary>
internal sealed class TenantActivityHubFilter : IHubFilter
{
    public static TenantActivityHubFilter Instance { get; } = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await TenantActivityCheck.ThrowIfInactiveAsync(
                invocationContext.ServiceProvider,
                "hubOptions.AddTenantry()",
                invocationContext.Context.ConnectionAborted)
            .ConfigureAwait(false);

        return await next(invocationContext).ConfigureAwait(false);
    }
}
