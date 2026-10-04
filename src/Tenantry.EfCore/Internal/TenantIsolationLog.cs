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
        "SaveChanges is writing tenant-owned entities ({EntityTypes}) without a resolved tenant. Updates and deletes " +
        "are not tenant-checked (EfCoreIsolationOptions.OnMissingTenant = Warn)",
        EventName = "WriteWithoutTenant")]
    public static partial void WriteWithoutTenant(ILogger logger, string entityTypes);

    [LoggerMessage(2003, LogLevel.Warning,
        "A {State} of tenant-owned entity '{EntityType}' in tenant '{TenantId}' matched no row. The row does not " +
        "exist, belongs to another tenant, or was changed concurrently",
        EventName = "WriteMatchedNoRow")]
    public static partial void WriteMatchedNoRow(ILogger logger, string state, string entityType, string? tenantId);

    [LoggerMessage(2004, LogLevel.Error,
        "A SaveChanges failed, or never ended, after sending statements in a transaction EF Core could not undo it in " +
        "(no savepoint, or an ambient transaction), and a save in it wrote rows that rely on another of its statements' " +
        "tenant check: the transaction was rolled back, not committed. {EntityType} is the entity whose check failed, " +
        "or the context's type when the failure was not a check. Catching a failed SaveChanges and going on in the " +
        "same transaction causes this; a transaction with savepoints avoids it",
        EventName = "TransactionNotCommitted")]
    public static partial void TransactionNotCommitted(ILogger logger, string entityType);

    [LoggerMessage(2005, LogLevel.Debug,
        "SaveChanges writes rows whose tenant is checked by another of its statements, so it runs in a transaction " +
        "although Database.AutoTransactionBehavior is Never (EfCoreIsolationOptions.OnSaveWithoutTransaction = UseTransaction)",
        EventName = "SaveInTransaction")]
    public static partial void SaveInTransaction(ILogger logger);

    [LoggerMessage(2006, LogLevel.Warning,
        "'{Context}' has entity types that are neither tenant-owned nor marked as shared across tenants: {EntityTypes} " +
        "(EfCoreIsolationOptions.OnUnmarkedEntityType = Warn). Logged once for each model EF Core builds",
        EventName = "UnmarkedEntityTypes")]
    public static partial void UnmarkedEntityTypes(ILogger logger, string context, string entityTypes);
}
