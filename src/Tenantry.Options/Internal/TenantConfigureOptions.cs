using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tenantry.Options.Internal;

/// <summary>
/// One <c>ConfigurePerTenant</c> step: applied to the default-named value after every ordinary <c>Configure</c>, while a
/// tenant is current, with the tenant (and, for a step that asks, the services of a scope of its own).
/// </summary>
internal sealed class TenantConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions, TKey>(
    ITenantContext<TKey> tenantContext,
    IServiceScopeFactory scopes,
    Action<TOptions, ITenantDescriptor, IServiceProvider?> configure,
    bool withServices)
    : IPostConfigureOptions<TOptions>
    where TOptions : class
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void PostConfigure(string? name, TOptions options)
    {
        if (name != Microsoft.Extensions.Options.Options.DefaultName || tenantContext.CurrentTenant is not { } tenant)
            return;

        if (!withServices)
        {
            configure(options, tenant, null);
            return;
        }

        using var scope = scopes.CreateScope();
        configure(options, tenant, scope.ServiceProvider);
    }
}
