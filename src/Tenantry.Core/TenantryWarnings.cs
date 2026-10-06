using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry;

/// <summary>
/// The warnings an application can turn off with <c>tenant.IgnoreWarnings(…)</c>: each reports, once, configuration
/// that may be deliberate. The documentation's diagnostics page lists every event Tenantry logs.
/// </summary>
public static class TenantryWarnings
{
    /// <summary>
    /// Event 2007, from Tenantry.EfCore: on SQL Server or MySQL, a model's <c>string</c> <c>TenantId</c> columns have
    /// no collation, so the database's default, which ignores case, compares tenant ids.
    /// </summary>
    public const int StringTenantIdCollation = 2007;

    /// <summary>
    /// Event 2008, from Tenantry.Options: <c>IOptions&lt;T&gt;</c> of a type configured per tenant was read while a
    /// tenant is current, and gave the ordinary value.
    /// </summary>
    public const int OrdinaryOptionsReadAsTenant = 2008;

    private static readonly int[] Ignorable = [StringTenantIdCollation, OrdinaryOptionsReadAsTenant];

    /// <summary>
    /// Whether the application turned off the warning <paramref name="eventId"/> with <c>IgnoreWarnings</c>. Tenantry's
    /// packages read it before they log one of the warnings this class names.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="eventId">The warning's event id.</param>
    /// <returns><see langword="true"/> when the warning is not to be logged.</returns>
    public static bool IsIgnored(IServiceProvider services, int eventId)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<Ignored>()?.EventIds.Contains(eventId) == true;
    }

    /// <summary>Registers <paramref name="eventIds"/> as ignored, after refusing any id this class does not name.</summary>
    internal static void Ignore(IServiceCollection services, int[] eventIds)
    {
        foreach (var eventId in eventIds)
        {
            if (!Ignorable.Contains(eventId))
            {
                throw new ArgumentException(
                    $"Tenantry cannot ignore event {eventId}. IgnoreWarnings accepts only " +
                    $"{string.Join(" and ", Ignorable)} (TenantryWarnings). " +
                    "Event 2002 is turned off with EfCoreIsolationOptions.OnMissingTenant, and event 2006 with " +
                    "EfCoreIsolationOptions.OnUnmarkedEntityType.",
                    nameof(eventIds));
            }
        }

        // Each call replaces the set with a new one that adds its ids, so a provider already built keeps its own.
        var registered = services
            .FirstOrDefault(d => d.ServiceType == typeof(Ignored) && !d.IsKeyedService)
            ?.ImplementationInstance as Ignored;

        IEnumerable<int> ids = [.. registered?.EventIds ?? FrozenSet<int>.Empty, .. eventIds];
        services.Replace(ServiceDescriptor.Singleton(new Ignored(ids)));
    }

    private sealed class Ignored(IEnumerable<int> eventIds)
    {
        public FrozenSet<int> EventIds { get; } = eventIds.ToFrozenSet();
    }
}
