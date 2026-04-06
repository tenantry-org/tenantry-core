using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Tenantry.Core;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Validates all entries in the change tracker on every <c>SaveChanges</c> call,
/// ensuring no entity from a different tenant scope is being modified or deleted.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// This validator is enabled by <c>builder.AddEfCoreIsolation(options =&gt; options.StrictIsolation = true)</c>.
/// It inspects <c>Added</c>, <c>Modified</c>, and <c>Deleted</c> entities —
/// not just <c>Added</c> ones — providing the strongest possible pre-write guarantee.
/// </remarks>
internal sealed class StrictIsolationValidator<TKey>(
    ILogger<StrictIsolationValidator<TKey>> logger)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Validates the change tracker entries against the current tenant context.
    /// </summary>
    /// <param name="entries">The entries from <c>DbContext.ChangeTracker.Entries()</c>.</param>
    /// <param name="tenantContext">The current tenant scope.</param>
    /// <exception cref="TenantIsolationViolationException">
    /// Thrown when any entry belongs to a tenant other than the current one.
    /// </exception>
    public void Validate(IEnumerable<EntityEntry> entries, ITenantContext<TKey> tenantContext)
    {
        if (!tenantContext.HasTenant)
        {
            return;
        }

        var currentTenantId = tenantContext.CurrentTenant!.TenantId;

        foreach (var entry in entries)
        {
            if (entry.Entity is not ITenantScoped<TKey> tenantEntity)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Added entities with an unset TenantId are safe — the interceptor will stamp them.
            // IsUnstamped treats both null and string.Empty as "not yet assigned", since C# developers
            // commonly initialise string properties to string.Empty rather than null.
            if (entry.State == EntityState.Added && IsUnstamped(tenantEntity.TenantId))
            {
                continue;
            }

            if (IsUnstamped(tenantEntity.TenantId) || tenantEntity.TenantId.Equals(currentTenantId))
            {
                continue;
            }

            IsolationDiagnostics diagnostics = new()
            {
                EntityTypeName = entry.Entity.GetType().Name,
                OffendingTenantId = tenantEntity.TenantId.ToString()!,
                ExpectedTenantId = currentTenantId.ToString()!,
            };

            logger.LogError(
                "Strict isolation violation: {EntityTypeName} has TenantId={OffendingTenantId} " +
                "but current scope is {ExpectedTenantId}. Detected at {DetectedAt}",
                diagnostics.EntityTypeName,
                diagnostics.OffendingTenantId,
                diagnostics.ExpectedTenantId,
                diagnostics.DetectedAt);

            throw new TenantIsolationViolationException(
                diagnostics.EntityTypeName,
                diagnostics.OffendingTenantId,
                diagnostics.ExpectedTenantId);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="tenantId"/> represents an unset/unstamped value.
    /// For reference types this is <see langword="null"/>; for <see cref="string"/> it also
    /// covers <see cref="string.Empty"/>, since C# developers commonly initialise string
    /// properties to <c>string.Empty</c> rather than <see langword="null"/> to satisfy
    /// nullable-reference-type analysis.
    /// </summary>
    private static bool IsUnstamped(TKey tenantId) =>
        EqualityComparer<TKey>.Default.Equals(tenantId, default!) ||
        tenantId is string {Length: 0};
}
