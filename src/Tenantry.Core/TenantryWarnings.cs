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

    // Tenantry's own events are 1000 to 2999; an integration's start at 3000 (docs/diagnostics.md).
    private const int FirstIntegrationEventId = 3000;

    /// <summary>
    /// Whether the application turned off the warning <paramref name="eventId"/> with <c>IgnoreWarnings</c>. Tenantry's
    /// packages read it before they log one of the warnings this class names, and an integration before it logs one
    /// it registered.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="eventId">The warning's event id.</param>
    /// <returns><see langword="true"/> when the warning is not to be logged.</returns>
    public static bool IsIgnored(IServiceProvider services, int eventId)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<Ignored>()?.EventIds.Contains(eventId) == true;
    }

    /// <summary>
    /// Registers the warnings an integration logs that the application may turn off, so <c>IgnoreWarnings</c> accepts
    /// their event ids. Call it from the integration's registration method, which the application calls before
    /// <c>IgnoreWarnings</c>; the ids are from the range the documentation's diagnostics page leaves for integrations.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="eventIds">The event ids of the integration's warnings, 3000 or above.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="eventIds"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An id is below 3000, in the range of Tenantry's own events.</exception>
    public static void Register(IServiceCollection services, params int[] eventIds)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(eventIds);

        var own = Array.FindIndex(eventIds, eventId => eventId < FirstIntegrationEventId);
        if (own >= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventIds),
                eventIds[own],
                $"Event ids below {FirstIntegrationEventId} are Tenantry's own. An integration's start at {FirstIntegrationEventId}.");
        }

        Add(services, ids => new Registered(ids), eventIds);
    }

    /// <summary>
    /// Registers <paramref name="eventIds"/> as ignored, after refusing any id this class does not name and no
    /// integration registered.
    /// </summary>
    internal static void Ignore(IServiceCollection services, int[] eventIds)
    {
        var registered = Find<Registered>(services);

#pragma warning disable S3267 // The body throws, so with Where the loop could never run twice
        foreach (var eventId in eventIds)
#pragma warning restore S3267
        {
            if (!Ignorable.Contains(eventId) && !registered.Contains(eventId))
            {
                throw new ArgumentException(
                    $"Tenantry cannot ignore event {eventId}. IgnoreWarnings accepts {string.Join(" and ", Ignorable)} " +
                    "(TenantryWarnings) and the ids an integration registered before it was called" +
                    $" ({(registered.Count == 0 ? "none" : string.Join(", ", registered.Order()))}). The " +
                    "documentation's diagnostics page says how each other warning is turned off.",
                    nameof(eventIds));
            }
        }

        Add(services, ids => new Ignored(ids), eventIds);
    }

    // Each call replaces the set with a new one that adds its ids, so a provider already built keeps its own.
    private static void Add<TSet>(IServiceCollection services, Func<IEnumerable<int>, TSet> create, int[] eventIds)
        where TSet : EventIdSet
    {
        IEnumerable<int> ids = [.. Find<TSet>(services), .. eventIds];
        services.Replace(ServiceDescriptor.Singleton(create(ids)));
    }

    private static FrozenSet<int> Find<TSet>(IServiceCollection services)
        where TSet : EventIdSet =>
        (services.FirstOrDefault(d => d.ServiceType == typeof(TSet) && !d.IsKeyedService)?.ImplementationInstance as TSet)
        ?.EventIds ?? FrozenSet<int>.Empty;

    private abstract class EventIdSet(IEnumerable<int> eventIds)
    {
        public FrozenSet<int> EventIds { get; } = eventIds.ToFrozenSet();
    }

    private sealed class Ignored(IEnumerable<int> eventIds) : EventIdSet(eventIds);

    private sealed class Registered(IEnumerable<int> eventIds) : EventIdSet(eventIds);
}
