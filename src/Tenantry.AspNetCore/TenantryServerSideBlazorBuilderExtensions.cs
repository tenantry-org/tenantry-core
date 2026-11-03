using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.AspNetCore.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tenantry's check of each Blazor Server circuit's activity.
/// </summary>
public static class TenantryServerSideBlazorBuilderExtensions
{
    /// <summary>
    /// Ends a Blazor Server circuit whose tenant no longer exists or is no longer active, at the circuit's next browser
    /// event, JavaScript interop call or navigation, so suspending or deleting a tenant stops the circuits it already
    /// has open.
    /// </summary>
    /// <param name="builder">The builder <c>AddInteractiveServerComponents()</c> or <c>AddServerSideBlazor()</c> returns.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A circuit's tenant is the one <c>app.UseTenantry()</c> resolved for the request that opened its connection.
    /// Blazor runs each inbound activity on the circuit's current connection, and when it reconnects a circuit, that
    /// is a new connection, whose request may resolve another tenant or none (as it does for a tenant suspended in the
    /// meantime). So before each activity, a circuit handler first checks that the connection's tenant is the
    /// circuit's. A connection without a tenant throws <see cref="TenantNotResolvedException"/>, and one with another
    /// tenant, or with a tenant for a circuit that opened without one, <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// The handler then looks the circuit's tenant up again with <see cref="ITenantLookup{TKey}"/>, through the cache
    /// with <c>CacheTenants</c>, and checks it with <see cref="ITenantActivity{TKey}"/>. A tenant the store no longer
    /// has throws <see cref="TenantNotFoundException"/>, and one an activity validator refuses
    /// <see cref="TenantInactiveException"/>. A circuit that opened without a tenant, and still has none, is not
    /// checked.
    /// </para>
    /// <para>
    /// On .NET 10 and later, Blazor can pause a circuit (<c>Blazor.pauseCircuit()</c>, or when it evicts a disconnected
    /// one) and resume its state in a new circuit, whose tenant is the resuming request's. The handler records the
    /// circuit's tenant in the state Blazor keeps, and refuses the resumed circuit's first activity, which adds its
    /// components, when the request that resumed it resolved another tenant or none, or when the state records no
    /// tenant (a circuit paused without this handler): <see cref="TenantNotResolvedException"/> when the state records a
    /// tenant and the request resolved none, and <see cref="InvalidOperationException"/> otherwise. The circuit then
    /// ends before it renders anything.
    /// </para>
    /// <para>
    /// A refused activity does not run, and Blazor ends the circuit as it does for any unhandled exception.
    /// </para>
    /// <para>
    /// An activity throws <see cref="InvalidOperationException"/> if Tenantry is not registered.
    /// </para>
    /// </remarks>
    public static IServerSideBlazorBuilder AddTenantry(this IServerSideBlazorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, TenantActivityCircuitHandler>());
        return builder;
    }
}
