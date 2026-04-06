namespace Tenantry.Core.Exceptions;

/// <summary>
/// Thrown when a cross-tenant data isolation violation is detected during a
/// <c>SaveChanges</c> or <c>SaveChangesAsync</c> call.
/// This exception is raised <em>before</em> any changes are written to the database.
/// </summary>
public sealed class TenantIsolationViolationException : InvalidOperationException
{
    /// <summary>
    /// The CLR type name of the entity that caused the violation.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// The <c>TenantId</c> value found on the offending entity.
    /// </summary>
    public string OffendingTenantId { get; }

    /// <summary>
    /// The <c>TenantId</c> of the currently active tenant scope.
    /// </summary>
    public string ExpectedTenantId { get; }

    /// <summary>
    /// Initialises a new instance with full diagnostic context.
    /// </summary>
    /// <param name="entityTypeName">CLR type name of the violating entity.</param>
    /// <param name="offendingTenantId">TenantId found on the entity.</param>
    /// <param name="expectedTenantId">TenantId of the current tenant scope.</param>
    public TenantIsolationViolationException(
        string entityTypeName,
        string offendingTenantId,
        string expectedTenantId)
        : base(BuildMessage(entityTypeName, offendingTenantId, expectedTenantId))
    {
        EntityTypeName = entityTypeName;
        OffendingTenantId = offendingTenantId;
        ExpectedTenantId = expectedTenantId;
    }

    private static string BuildMessage(
        string entityTypeName,
        string offendingTenantId,
        string expectedTenantId) =>
        $"Tenant isolation violation detected on entity '{entityTypeName}'. " +
        $"The entity belongs to tenant '{offendingTenantId}' but the current scope is tenant '{expectedTenantId}'. " +
        $"This SaveChanges call has been aborted — no data was written to the database. " +
        $"Ensure that entities are only modified within the owning tenant's request scope.";
}
