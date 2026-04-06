using Microsoft.AspNetCore.Http;
using Tenantry.Core;

namespace Tenantry.AspNetCore;

/// <summary>
/// Builds a single tenant access validation group. All validators added to the group must pass.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
public interface ITenantAccessValidationGroupBuilder<out TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Validates that the current request is allowed to access the resolved tenant
    /// by matching the tenant against one or more claims of the specified type on the current request principal.
    /// Supports repeated claims with single tenant IDs and JSON array claim values.
    /// </summary>
    /// <param name="claimType">The claim type containing one or more allowed tenant identifiers.</param>
    /// <returns>The same builder for chaining.</returns>
    ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccessByClaim(string claimType);

    /// <summary>
    /// Adds a synchronous tenant access validator to the group.
    /// </summary>
    /// <param name="validator">The synchronous validator to add.</param>
    /// <returns>The same builder for chaining.</returns>
    ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, bool> validator);

    /// <summary>
    /// Adds an asynchronous tenant access validator to the group.
    /// </summary>
    /// <param name="validator">The asynchronous validator to add.</param>
    /// <returns>The same builder for chaining.</returns>
    ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator);
}
