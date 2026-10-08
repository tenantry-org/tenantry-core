namespace Tenantry.EfCore;

/// <summary>
/// Thrown when EF Core would read or write across tenants. <see cref="Kind"/> says which check failed. Nothing has
/// been written; a refused commit is rolled back.
/// </summary>
public sealed class TenantIsolationViolationException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="kind">Which isolation check failed.</param>
    /// <param name="typeName">The CLR type name of the entity the check concerns, or of the <c>DbContext</c> for a check of the whole context.</param>
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
    /// The CLR type name of the entity the check concerns, or of the <c>DbContext</c> for a check of the whole
    /// context: <see cref="TenantIsolationViolationKind.TenantDatabaseMismatch"/>,
    /// <see cref="TenantIsolationViolationKind.TenantSchemaMismatch"/>,
    /// <see cref="TenantIsolationViolationKind.SaveWithoutTransaction"/>,
    /// <see cref="TenantIsolationViolationKind.ModelConfiguration"/> for unmarked entity types, and
    /// <see cref="TenantIsolationViolationKind.TransactionRolledBack"/> when no single entity's check failed.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// The tenant the rejected entity or database belongs to, as a string for logging, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Tenantry sets it for <see cref="TenantIsolationViolationKind.EntityWrite"/>, except when it could not read the
    /// row's tenant: a join row saved without the tenant-owned row it joins, an owned entity saved without its owner,
    /// or an entity with no row stored for the current tenant. It also sets it for
    /// <see cref="TenantIsolationViolationKind.TenantDatabaseMismatch"/> when the context was connected to a tenant's
    /// database. It is <see langword="null"/> for every other kind Tenantry throws, and for those Tenantry.Pro throws.
    /// </remarks>
    public string? OffendingTenantId { get; }

    /// <summary>
    /// The current tenant, as a string for logging, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Tenantry sets it for <see cref="TenantIsolationViolationKind.EntityWrite"/>, and for
    /// <see cref="TenantIsolationViolationKind.TenantDatabaseMismatch"/> when a tenant is current. It is
    /// <see langword="null"/> for every other kind Tenantry throws. Tenantry.Pro sets it for the
    /// <see cref="TenantIsolationViolationKind.TenantSchemaMismatch"/> it throws, and for the
    /// <see cref="TenantIsolationViolationKind.ModelConfiguration"/> it throws for a Shared tenant's context.
    /// </remarks>
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
    /// The model cannot isolate a tenant-owned entity type (for example a missing tenant filter, a base type or owner
    /// that is not tenant-owned, or a tenant-owned type mapped to JSON), or, under
    /// <see cref="EfCoreIsolationOptions.OnUnmarkedEntityType"/> = <c>Reject</c>, has entity types that are neither
    /// tenant-owned nor marked as shared. The message names the types and the cause.
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
    /// A save that sent statements in this transaction failed or never reported success, and a save in it wrote rows
    /// whose tenant check is another of its statements. EF Core could not undo only the failed save (no savepoint, a
    /// failed rollback to its savepoint, or an ambient transaction), so the transaction was rolled back instead of
    /// committed.
    /// </summary>
    TransactionRolledBack,

    /// <summary>
    /// A schema-per-tenant context would use a schema other than the current tenant's, such as one built for the
    /// tenant that was current when it was first used. Thrown by packages that put tenants in schemas of their own,
    /// such as Tenantry.Pro, from a <see cref="TenantContextGuard"/>.
    /// </summary>
    TenantSchemaMismatch,
}
