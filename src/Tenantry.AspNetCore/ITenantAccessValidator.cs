using Microsoft.AspNetCore.Http;

namespace Tenantry.AspNetCore;

/// <summary>
/// Decides whether a request may use the tenant it names.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
/// Register one with <c>tenant.ValidateTenantAccess&lt;TValidator&gt;()</c>. It is created in each request's scope, so
/// it can depend on scoped services such as a <c>DbContext</c>. Every validator must allow a request before its tenant
/// is made current. A request refused by one gets <see cref="TenantResolutionOptions{TKey}.AccessDeniedStatusCode"/> on
/// an endpoint that needs a tenant, and continues without a tenant on any other. With <c>app.UseTenantResolution()</c>,
/// a signed-in request it refuses gets that status on every endpoint.
/// </remarks>
/// <example>
/// <code>
/// public sealed class MembershipValidator(AppDbContext db) : ITenantAccessValidator&lt;Guid&gt;
/// {
///     public async ValueTask&lt;bool&gt; ValidateAsync(HttpContext context, ITenantDescriptor&lt;Guid&gt; tenant, CancellationToken ct) =&gt;
///         await db.Memberships.AnyAsync(m =&gt; m.UserId == context.User.FindFirstValue("sub") &amp;&amp; m.TenantId == tenant.TenantId, ct);
/// }
/// </code>
/// </example>
public interface ITenantAccessValidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns <see langword="true"/> when the request may use <paramref name="tenant"/>.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="tenant">The tenant the request names, found in the tenant store.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    ValueTask<bool> ValidateAsync(HttpContext context, ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken);
}
