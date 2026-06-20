using Microsoft.AspNetCore.Http;
using Tenantry.Core;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Builds a single tenant access validation group. All validators added to the group must pass.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
internal sealed class TenantAccessValidationGroupBuilder<TKey> : ITenantAccessValidationGroupBuilder<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly List<Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>> _validators = [];

    /// <summary>
    /// Validates that the current request is allowed to access the resolved tenant
    /// by matching the tenant against one or more claims of the specified type on the current request principal.
    /// Supports repeated claims with single tenant IDs and JSON array claim values.
    /// </summary>
    /// <param name="claimType">The claim type containing one or more allowed tenant identifiers.</param>
    public ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccessByClaim(string claimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        return ValidateTenantAccess((httpContext, tenant, _) =>
            ClaimTenantAccessValidator.ValidateAsync(httpContext, tenant, claimType));
    }

    /// <summary>
    /// Adds a synchronous tenant access validator to the group.
    /// </summary>
    public ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, bool> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        return ValidateTenantAccess((httpContext, tenant, _) =>
            ValueTask.FromResult(validator(httpContext, tenant)));
    }

    /// <summary>
    /// Adds an asynchronous tenant access validator to the group.
    /// </summary>
    public ITenantAccessValidationGroupBuilder<TKey> ValidateTenantAccess(
        Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        _validators.Add(validator);
        return this;
    }

    internal IReadOnlyList<Func<HttpContext, ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>>> Build()
    {
        return _validators.Count == 0 
            ? throw new InvalidOperationException("Tenant access validation groups must contain at least one validator.") 
            : _validators;
    }
}
