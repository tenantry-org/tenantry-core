using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The EF Core isolation's log messages, under the category <c>Tenantry.EfCore</c>. Their event ids are stable: the
/// documentation lists them, so applications can alert on them (2001, an isolation violation, above all).
/// </summary>
internal static partial class TenantIsolationLog
{
    public const string Category = "Tenantry.EfCore";

    public static ILogger? Find(IServiceProvider? services) =>
        services?.GetService<ILoggerFactory>()?.CreateLogger(Category);

    [LoggerMessage(2001, LogLevel.Error,
        "Tenant isolation violation: entity '{EntityType}' belongs to tenant '{OffendingTenantId}' but the current " +
        "tenant is '{ExpectedTenantId}'. Aborting SaveChanges",
        EventName = "TenantIsolationViolation")]
    public static partial void IsolationViolation(ILogger logger, string entityType, string? offendingTenantId, string? expectedTenantId);

    [LoggerMessage(2002, LogLevel.Warning,
        "SaveChanges is writing tenant-scoped entities ({EntityTypes}) without a resolved tenant. Updates and deletes " +
        "are not tenant-checked (EfCoreIsolationOptions.OnMissingTenant = Warn)",
        EventName = "WriteWithoutTenant")]
    public static partial void WriteWithoutTenant(ILogger logger, string entityTypes);

    [LoggerMessage(2003, LogLevel.Warning,
        "A {State} of tenant-scoped entity '{EntityType}' in tenant '{TenantId}' matched no row. The row does not " +
        "exist, belongs to another tenant, or was changed concurrently",
        EventName = "WriteMatchedNoRow")]
    public static partial void WriteMatchedNoRow(ILogger logger, string state, string entityType, string? tenantId);

    [LoggerMessage(2004, LogLevel.Information,
        "Entity '{EntityType}' has an unnamed query filter, so Tenantry merged its tenant filter into it: " +
        "IgnoreQueryFilters([TenantryQueryFilters.Tenant]) cannot remove the tenant filter alone for it. Name the " +
        "entity's filter to keep the two apart",
        EventName = "TenantFilterMerged")]
    public static partial void TenantFilterMerged(ILogger logger, string entityType);
}
