using Microsoft.Extensions.Logging;
using Tenantry.Core;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Logs a structured warning when an EF Core query or <c>SaveChanges</c> executes
/// without a resolved tenant context.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// A missing tenant context is a common source of bugs: endpoints that bypass the
/// tenant middleware, background jobs without tenant propagation, or misconfigured
/// middleware ordering. This detector surfaces those issues early.
/// </remarks>
internal sealed class MissingContextDetector<TKey>(ILogger<MissingContextDetector<TKey>> logger)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Checks whether a tenant is resolved and logs a warning if not.
    /// </summary>
    /// <param name="tenantContext">The current tenant scope.</param>
    /// <param name="operationDescription">
    /// A short description of the operation being performed (e.g. "SaveChanges", "Query on Orders").
    /// </param>
    /// <returns>
    /// <c>true</c> if a tenant is resolved; <c>false</c> if the context is missing.
    /// </returns>
    public bool CheckAndWarn(ITenantContext<TKey> tenantContext, string operationDescription)
    {
        if (tenantContext.HasTenant)
        {
            return true;
        }

        logger.LogWarning(
            "Operation '{Operation}' executed without a resolved tenant context. " +
            "Query filters will not be applied and TenantId will not be stamped. " +
            "If this endpoint requires tenant isolation, ensure app.UseMultiTenancy() " +
            "is registered before this middleware in the pipeline",
            operationDescription);

        return false;
    }
}
