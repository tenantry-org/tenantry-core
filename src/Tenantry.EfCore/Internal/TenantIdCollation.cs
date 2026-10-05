using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Warns, as a model is built, when the database compares <c>string</c> tenant ids under a default collation that
/// ignores case (event 2007): on SQL Server or MySQL, in a table whose text <c>TenantId</c> column, its table and the
/// model have no collation. PostgreSQL's and SQLite's defaults compare exactly (SQLite's is <c>BINARY</c>), so they
/// are not checked.
/// </summary>
/// <remarks>
/// Any collation the provider applies counts: Tenantry does not judge its name. A <c>string</c> <c>TenantId</c> that a
/// value converter stores as another type is not compared as text. Other key types are not checked: a <c>Guid</c> or
/// number stored as text is written in one case.
/// </remarks>
internal static class TenantIdCollation
{
    // DatabaseFacade.ProviderName of SQL Server's provider, Pomelo's for MySQL, and Oracle's.
    private const string OracleMySql = "MySql.EntityFrameworkCore";

    private static readonly string[] CaseInsensitiveByDefault =
        ["Microsoft.EntityFrameworkCore.SqlServer", "Pomelo.EntityFrameworkCore.MySql", OracleMySql];

    // EF Core's UseCollation on a model, column or (Pomelo's) table. Oracle's provider does not apply it: only its own
    // ForMySQLHasCollation, on a column or table.
    private static readonly string[] RelationalCollation = [RelationalAnnotationNames.Collation];
    private static readonly string[] OracleCollation = ["MySQL:Collation"];

    /// <param name="model">The model being built, whose tenant key type is <c>string</c>.</param>
    /// <param name="context">The context the model is built for.</param>
    /// <param name="services">
    /// The context's application service provider, or <see langword="null"/> when its options have none.
    /// </param>
    public static void WarnIfCaseInsensitive(IReadOnlyModel model, DbContext context, IServiceProvider? services)
    {
        var provider = context.Database.ProviderName;
        var annotations = provider == OracleMySql ? OracleCollation : RelationalCollation;

        if (services is null ||
            !CaseInsensitiveByDefault.Contains(provider) ||
            HasCollation(model, annotations) ||
            TenantryWarnings.IsIgnored(services, TenantryWarnings.StringTenantIdCollation) ||
            TenantIsolationLog.Find(services) is not { } logger)
        {
            return;
        }

        // The tables of entity types with a collation of their own, which a TenantId column in them follows.
        var collated = model.GetEntityTypes()
            .Where(type => HasCollation(type, annotations))
            .Select(type => StoreObjectIdentifier.Create(type, StoreObjectType.Table))
            .OfType<StoreObjectIdentifier>()
            .ToHashSet();
        SortedSet<string> tables = new(StringComparer.Ordinal);

        foreach (var entityType in model.GetEntityTypes().Where(type => TenantEntityTypes.IsTenantEntity(type.ClrType)))
        {
            if (entityType.FindProperty(TenantOwnership.TenantIdProperty) is not { } tenantId ||
                HasCollation(tenantId, annotations) ||
                StoredType(tenantId) != typeof(string))
            {
                continue;
            }

            // Under TPT, TenantId has a column in the root's table only, not in a derived type's; under entity
            // splitting, in one of the type's tables.
            foreach (var table in Tables(entityType)
                         .Where(table => !collated.Contains(table) && tenantId.GetColumnName(table) is not null))
            {
                tables.Add(table.DisplayName());
            }
        }

        if (tables.Count > 0)
        {
            TenantIsolationLog.StringTenantIdCollation(logger, context.GetType().Name, string.Join(", ", tables));
        }
    }

    private static bool HasCollation(IReadOnlyAnnotatable annotatable, string[] annotations) =>
        annotations.Any(name => annotatable.FindAnnotation(name)?.Value is not null);

    // The entity type's table and, under entity splitting, the tables it is split to.
    private static IEnumerable<StoreObjectIdentifier> Tables(IReadOnlyEntityType entityType) =>
        StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is { } table
            ? entityType.GetMappingFragments(StoreObjectType.Table)
                .Select(fragment => fragment.StoreObject)
                .Prepend(table)
            : [];

    // The type the database stores: a value converter's provider type, or the property's own.
    private static Type StoredType(IReadOnlyProperty property) =>
        property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType;
}
