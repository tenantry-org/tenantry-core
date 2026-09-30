using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenantry;

namespace Tenantry.Tests.Shared;

/// <summary>
/// The conformance check each package's tests run on a host built from the package's public registration methods,
/// the way an application builds one: scopes are validated, and every registration is checked when the provider is
/// built. The check then resolves every Tenantry service in a scope and starts the host, so a captive dependency, a
/// constructor dependency injection cannot satisfy, or a hosted service that fails to start shows up here rather than
/// in an application's first run. (This file is linked into each test project.)
/// </summary>
internal static class Conformance
{
    /// <summary>The provider options of a host in the Development environment, set explicitly.</summary>
    public static ServiceProviderOptions ProviderOptions { get; } = new() { ValidateScopes = true, ValidateOnBuild = true };

    /// <summary>
    /// Resolves from <paramref name="scope"/> every registration in <paramref name="services"/> whose service type is
    /// in a Tenantry namespace (open generics and keyed services aside).
    /// </summary>
    public static void ResolveEveryTenantryService(IServiceCollection services, IServiceProvider scope)
    {
        var serviceTypes = services
            .Where(descriptor => !descriptor.IsKeyedService &&
                                 !descriptor.ServiceType.IsGenericTypeDefinition &&
                                 descriptor.ServiceType.Namespace?.StartsWith("Tenantry", StringComparison.Ordinal) == true)
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();

        serviceTypes.Should().NotBeEmpty();

        foreach (var serviceType in serviceTypes)
        {
            scope.GetServices(serviceType).Should().NotBeEmpty($"{serviceType} is registered")
                .And.NotContainNulls($"{serviceType} is registered");
        }
    }

    /// <summary>Starts the host, which runs its hosted services, and stops it.</summary>
    public static async Task StartAndStopAsync(IHost host)
    {
        await host.StartAsync();
        await host.StopAsync();
    }
}

/// <summary>
/// A store registered with <c>UseStore</c>, so scoped, with a scoped dependency of its own, as a store backed by a
/// <c>DbContext</c> has. Knows the tenants "acme" and "globex".
/// </summary>
internal sealed class ScopedTenantStore(ScopedTenantStore.Session session) : ITenantStore<string>
{
    private static readonly ITenantDescriptor<string>[] Tenants =
    [
        new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" },
        new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" },
    ];

    public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(session.Open ? Tenants.FirstOrDefault(tenant => tenant.TenantId == tenantId) : null);

    public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>(Tenants);

    /// <summary>The store's scoped dependency; register it with <c>AddScoped</c>.</summary>
    public sealed class Session
    {
        public bool Open => true;
    }
}
