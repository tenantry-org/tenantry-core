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
    /// Event 3001, from Tenantry.Options: <c>IOptions&lt;T&gt;</c> of a type configured per tenant was read while a
    /// tenant is current, and gave the ordinary value.
    /// </summary>
    public const int OrdinaryOptionsReadAsTenant = 3001;

    // Tenantry's packages log events 1000 to 2999, and Tenantry.Options event 3001. An id another package logs takes
    // effect only when that package reads IsIgnored, and none does yet: Tenantry.Pro's events are filtered by their
    // logging category.
    private const int FirstEventId = 1000;
    private const int LastEventId = 2999;

    private static readonly int[] Ignorable = [StringTenantIdCollation, OrdinaryOptionsReadAsTenant];

    /// <summary>
    /// Whether the application turned off the warning <paramref name="eventId"/> with <c>IgnoreWarnings</c>. A package
    /// that logs warnings of its own reads it here for its ids outside Tenantry's events, 1000 to 2999 and 3001.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="eventId">The warning's event id.</param>
    /// <returns><see langword="true"/> when the warning is not to be logged.</returns>
    public static bool IsIgnored(IServiceProvider services, int eventId)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<Ignored>()?.EventIds.Contains(eventId) == true;
    }

    /// <summary>Registers <paramref name="eventIds"/> as ignored, after refusing Tenantry's other events.</summary>
    internal static void Ignore(IServiceCollection services, int[] eventIds)
    {
        foreach (var eventId in eventIds)
        {
            if (eventId is >= FirstEventId and <= LastEventId && !Ignorable.Contains(eventId))
            {
                throw new ArgumentException(
                    $"Tenantry cannot ignore event {eventId}. Of Tenantry's own events ({FirstEventId} to " +
                    $"{LastEventId}, and {OrdinaryOptionsReadAsTenant}), IgnoreWarnings accepts only " +
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
