namespace Tenantry.EfCore;

/// <summary>
/// Thrown when EF Core would read or write across tenants: before <c>SaveChanges</c> writes another tenant's
/// entity, before an <c>ExecuteUpdate</c> that could move rows between tenants, before a database-per-tenant
/// context uses another tenant's database, on the first use of a model that does not isolate a tenant-owned
/// entity type, before a save that must succeed or fail as a whole runs without a transaction it may not begin, or
/// instead of committing a transaction that holds a save whose tenant check failed and could not be undone. Nothing
/// has been written when it is thrown, or, for a commit, kept: the transaction is rolled back. <see cref="Kind"/> says
/// which.
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
    /// A database-per-tenant context would use a connection that was not set for it (and, pooled, for its current
    /// lease) or for the current tenant.
    /// </summary>
    TenantDatabaseMismatch,

    /// <summary>
    /// The model does not isolate a tenant-owned entity type: it has no tenant query filter or <c>TenantId</c>
    /// concurrency token, it uses another tenant key type, or it inherits from or is owned by an entity type that
    /// is not tenant-owned; an entity type that is not tenant-owned shares its table; or an owned type's writes cannot
    /// be checked through its owner: it has no <c>TenantId</c>
    /// of its own and a key that does not include its owner's, it is owned through a key of a tenant-owned type
    /// that is neither its primary key nor includes its <c>TenantId</c>, or it is tenant-owned and mapped to JSON.
    /// </summary>
    ModelConfiguration,

    /// <summary>
    /// <c>SaveChanges</c> would write rows whose tenant check is another of its statements without a transaction
    /// (<c>Database.AutoTransactionBehavior</c> is <c>Never</c>), and
    /// <see cref="EfCoreIsolationOptions.OnSaveWithoutTransaction"/> is
    /// <see cref="SaveWithoutTransactionBehavior.Reject"/>.
    /// </summary>
    SaveWithoutTransaction,

    /// <summary>
    /// A transaction was about to commit, or an ambient one to complete, holding a <c>SaveChanges</c> that failed, or
    /// did not end, after sending some of its statements, among them rows whose tenant another of its statements
    /// checks, and EF Core could not undo that save in it (no savepoint, or an ambient transaction). It was rolled back
    /// instead.
    /// </summary>
    TransactionRolledBack,

    /// <summary>
    /// A schema-per-tenant context would use a schema other than the current tenant's, such as one built for the
    /// tenant that was current when it was first used. Thrown by packages that put tenants in schemas of their own,
    /// from a <see cref="TenantContextGuard"/>.
    /// </summary>
    TenantSchemaMismatch,
}
