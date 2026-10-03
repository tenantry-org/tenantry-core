using Tenantry.Internal;

namespace Tenantry;

/// <summary>
/// The names Tenantry records a tenant under in traces and logs: Tenantry.AspNetCore's <c>app.UseTenantry()</c> for a
/// request, and Tenantry.Pro for jobs, messages and background work. Use them to record the tenant the same way in your
/// own code, or to query and alert on it.
/// </summary>
public static class TenantTelemetry
{
    /// <summary>
    /// The tag that carries the tenant's id on a span (an <see cref="System.Diagnostics.Activity"/>) or a metric:
    /// <c>tenant.id</c>, the id formatted by <see cref="TenantIds.Format{TKey}"/>.
    /// </summary>
    public const string TenantIdTag = "tenant.id";

    /// <summary>
    /// The property that carries the tenant's id in a log scope: <c>TenantId</c>, the id formatted by
    /// <see cref="TenantIds.Format{TKey}"/>.
    /// </summary>
    public const string LogScopeName = "TenantId";

    /// <summary>
    /// The state of a log scope with one property, <see cref="LogScopeName"/>, for <c>ILogger.BeginScope</c>. A
    /// logging provider that records scopes (Serilog's, or the console's and OpenTelemetry's with
    /// <c>IncludeScopes</c>) adds the property to every entry written while the scope is open; its text is
    /// <c>TenantId:&lt;id&gt;</c>.
    /// </summary>
    /// <param name="tenantId">The tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>.</param>
    /// <returns>The scope's state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    /// <example>
    /// <code>
    /// using (logger.BeginScope(TenantTelemetry.CreateLogScope(TenantIds.Format(tenantId))))
    /// {
    ///     logger.LogInformation("Invoicing");   // carries TenantId
    /// }
    /// </code>
    /// </example>
    public static IReadOnlyList<KeyValuePair<string, object?>> CreateLogScope(string tenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        return new TenantLogScope(tenantId);
    }
}
