using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Http.Internal;

/// <summary>
/// Checks, as the host starts, that a client marked with <c>UseTenantry()</c> can be created: that Tenantry is
/// registered and that the client has an address to limit the tenant to. The client would otherwise fail only when it
/// is first created.
/// </summary>
/// <remarks>
/// It runs through <c>ValidateOnStart</c>, one named check per client, which every .NET host runs before it starts.
/// A service provider built without a host does not run it; the client's handler checks the same when it is created.
/// </remarks>
#pragma warning disable S1118 // The options factory creates it, so it cannot be static or privately constructed
internal sealed class PropagationCheck
#pragma warning restore S1118
{
    public static void Register(IServiceCollection services, string name, Uri? serviceAddress) =>
        services.AddOptions<PropagationCheck>(name)
            .Validate<IServiceProvider>((_, provider) =>
            {
                using var handler = TenantPropagationHandler.Create(provider, name, serviceAddress);
                return true;
            })
            .ValidateOnStart();
}
