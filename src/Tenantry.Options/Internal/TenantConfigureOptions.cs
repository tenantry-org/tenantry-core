using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Options.Internal;

/// <summary>A step of <c>ConfigurePerTenant</c>, which the options factory applies while a tenant is current.</summary>
internal interface ITenantOptionsStep<in TOptions>
    where TOptions : class
{
    void Apply(string name, TOptions options);
}

/// <summary>
/// One <see cref="TenantOptionsBuilder{TKey}"/> <c>Configure</c> or <c>ConfigureAll</c> step: applied to the options of its name (or of every
/// name), while a tenant is current, with the tenant (and, for a step that asks, the services of a scope of its own).
/// </summary>
internal sealed class TenantConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions, TKey>(
    ITenantContext<TKey> tenantContext,
    IServiceScopeFactory scopes,
    string? optionsName,
    Action<TOptions, ITenantDescriptor<TKey>, IServiceProvider?> configure,
    bool withServices)
    : ITenantOptionsStep<TOptions>
    where TOptions : class
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Apply(string name, TOptions options)
    {
        if ((optionsName is not null && name != optionsName) || tenantContext.CurrentTenant is not { } tenant)
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
