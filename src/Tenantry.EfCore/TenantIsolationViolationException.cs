namespace Tenantry.EfCore;

/// <summary>
/// Thrown when EF Core would read or write across tenants: before <c>SaveChanges</c> writes another tenant's
/// entity, before an <c>ExecuteUpdate</c> that could move rows between tenants, before a pooled database-per-tenant
/// context uses another tenant's database, or on the first use of a model that does not isolate a tenant-owned
/// entity type. Nothing has been written when it is thrown. <see cref="Kind"/> says which.
/// </summary>
public sealed class TenantIsolationViolationException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="kind">Which isolation check failed.</param>
    /// <param name="typeName">The CLR type name of the entity, or of the <c>DbContext</c> for <see cref="TenantIsolationViolationKind.TenantDatabaseMismatch"/>.</param>
    /// <param name="message">Why the operation was rejected.</param>
    /// <param name="offendingTenantId">The tenant the rejected entity or database belongs to, when known.</param>
    /// <param name="expectedTenantId">The current tenant, when known.</param>
    public TenantIsolationViolationException(
        TenantIsolationViolationKind kind,
        string typeName,
        string message,
        string? offendingTenantId = null,
        string? expectedTenantId = null)
        : base(message)
    {
        Kind = kind;
        TypeName = typeName;
        OffendingTenantId = offendingTenantId;
        ExpectedTenantId = expectedTenantId;
    }

    /// <summary>Which isolation check failed.</summary>
    public TenantIsolationViolationKind Kind { get; }

    /// <summary>
    /// The CLR type name of the entity that caused the violation, or of the <c>DbContext</c> for
    /// <see cref="TenantIsolationViolationKind.TenantDatabaseMismatch"/>.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// The tenant the rejected entity or database belongs to, or <see langword="null"/> when the check does not
    /// know one (a bulk update, a model check).
    /// </summary>
    public string? OffendingTenantId { get; }

    /// <summary>
    /// The current tenant, or <see langword="null"/> when none is current or the check does not use it.
    /// </summary>
    public string? ExpectedTenantId { get; }
}

/// <summary>
/// Which isolation check threw a <see cref="TenantIsolationViolationException"/>.
/// </summary>
public enum TenantIsolationViolationKind
{
    /// <summary>
    /// <c>SaveChanges</c> would write an entity of another tenant: a new entity that names another tenant, or a
    /// changed or deleted entity that was loaded as, or now names, another tenant.
    /// </summary>
    EntityWrite,

    /// <summary>
    /// An <c>ExecuteUpdate</c> would set <c>TenantId</c>, or sets a property the guard cannot identify.
    /// </summary>
    BulkUpdate,

    /// <summary>
    /// A pooled database-per-tenant context would use a connection that was not set for its current lease and
    /// the current tenant.
    /// </summary>
    TenantDatabaseMismatch,

    /// <summary>
    /// The model does not isolate a tenant-owned entity type: it has no tenant query filter or <c>TenantId</c>
    /// concurrency token, it uses another tenant key type, or it inherits from or is owned by an entity type that
    /// is not tenant-owned; an entity type that is not tenant-owned shares its table; or an owned type's writes cannot
    /// be checked through its owner: it has no <c>TenantId</c>
    /// of its own and a key that does not include its owner's, or it is owned through a key of a tenant-owned type
    /// that is neither its primary key nor includes its <c>TenantId</c>.
    /// </summary>
    ModelConfiguration,
}
