using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Internal;

/// <summary>
/// The default <see cref="ITenantActivity{TKey}"/>: asks every registered validator, in registration order.
/// </summary>
internal sealed class TenantActivity<TKey>(IEnumerable<ITenantActivityValidator<TKey>> validators) : ITenantActivity<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ITenantActivityValidator<TKey>[] _validators = [.. validators];

    public async ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        foreach (var validator in _validators)
        {
            if (!await validator.IsActiveAsync(tenant, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    public async ValueTask ThrowIfInactiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync(tenant, cancellationToken).ConfigureAwait(false))
        {
            throw new TenantInactiveException(tenant.TenantId);
        }
    }

    // This singleton keeps its validators for the application's lifetime, so one registered as scoped or transient
    // would be resolved once, from the root provider, and shared by every request. It is checked when the activity is
    // first resolved, so that a registration made after AddTenantry counts.
    internal static void Register(IServiceCollection services) =>
        services.TryAddSingleton<ITenantActivity<TKey>>(sp =>
        {
            RequireSingletonValidators(services);
            return new TenantActivity<TKey>(sp.GetServices<ITenantActivityValidator<TKey>>());
        });

    private static void RequireSingletonValidators(IServiceCollection services)
    {
        var validator = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ITenantActivityValidator<TKey>) && !d.IsKeyedService &&
            d.Lifetime != ServiceLifetime.Singleton);

        if (validator is not null)
        {
            var key = typeof(TKey).Name;
            throw new InvalidOperationException(
                $"ITenantActivity<{key}> is a singleton and keeps its validators, and the " +
                $"ITenantActivityValidator<{key}> {validator.ImplementationType?.Name ?? "from a factory"} is " +
                $"registered as {validator.Lifetime.ToString().ToLowerInvariant()}. Register it as a singleton, with " +
                "tenant.ValidateTenantActivity<TValidator>(). Read the tenant's status from the descriptor the store " +
                "returns, or create a scope inside the validator for a scoped service it needs.");
        }
    }
}

/// <summary>An <see cref="ITenantActivityValidator{TKey}"/> over a delegate.</summary>
internal sealed class DelegateTenantActivityValidator<TKey>(
    Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> isActive) : ITenantActivityValidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken) =>
        isActive(tenant, cancellationToken);
}

/// <summary>Registers an activity validator type for the builder's key type, as a singleton.</summary>
internal sealed class TenantActivityValidatorRegistration<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator> : ITenantRegistration
    where TValidator : class
{
    public void Apply<TKey>(ITenantBuilder<TKey> tenant)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (!typeof(ITenantActivityValidator<TKey>).IsAssignableFrom(typeof(TValidator)))
        {
            throw new InvalidOperationException(
                $"{typeof(TValidator).Name} does not implement ITenantActivityValidator<{typeof(TKey).Name}>, the " +
                $"activity validator of this application's tenant key type '{typeof(TKey).Name}'.");
        }

        tenant.Services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(ITenantActivityValidator<TKey>), typeof(TValidator)));
    }
}
