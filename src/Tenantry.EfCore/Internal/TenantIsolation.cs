using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// The parts of tenant isolation that need the tenant key type: the tenant query filters, and the checks on
/// <c>SaveChanges</c>. <c>UseTenantry()</c> takes no key type, so it comes from the model's tenant-owned entity types.
/// </summary>
internal abstract class TenantIsolation
{
    private static readonly ConcurrentDictionary<Type, TenantIsolation> ByKeyType = new();

    /// <summary>The tenant key type.</summary>
    public abstract Type KeyType { get; }

    /// <summary>Whether <paramref name="type"/> is tenant-owned: it is or implements <see cref="ITenantEntity{TKey}"/> for this key type.</summary>
    public abstract bool IsTenantEntity(Type type);

    /// <summary>
    /// The isolation for the tenant key type of <paramref name="model"/>'s tenant-owned entity types, or
    /// <see langword="null"/> when it has none.
    /// </summary>
    /// <exception cref="TenantIsolationViolationException">The entity types use more than one tenant key type.</exception>
    public static TenantIsolation? ForModel(IReadOnlyModel model)
    {
        Type? keyType = null;
        string? keyTypeEntity = null;

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var key in TenantEntityTypes.KeyTypes(entityType.ClrType))
            {
                if (keyType is null)
                {
                    keyType = key;
                    keyTypeEntity = entityType.ClrType.Name;
                }
                else if (key != keyType)
                {
                    throw new TenantIsolationViolationException(
                        TenantIsolationViolationKind.ModelConfiguration,
                        entityType.ClrType.Name,
                        $"Entity '{entityType.ClrType.Name}' implements ITenantEntity<{key.Name}>, but '{keyTypeEntity}' " +
                        $"implements ITenantEntity<{keyType.Name}>. An application uses one tenant key type, the one it " +
                        "registers with AddTenantry: use it for every tenant-owned entity.");
                }
            }
        }

        return keyType is null ? null : ByKeyType.GetOrAdd(keyType, Create);
    }

    // A model built at run time needs dynamic code anyway (DbContext's constructors say so), and the key type comes
    // from an ITenantEntity<TKey> the model maps, which has the same constraints as TenantIsolation<TKey>.
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only reached from a DbContext, which requires dynamic code.")]
    private static TenantIsolation Create(Type keyType) =>
        (TenantIsolation)Activator.CreateInstance(typeof(TenantIsolation<>).MakeGenericType(keyType))!;

    /// <summary>
    /// Adds the tenant query filter to every tenant-owned root entity type, and makes each one's <c>TenantId</c> a
    /// concurrency token.
    /// </summary>
    /// <param name="modelBuilder">The model builder, after <c>OnModelCreating</c> and the model contributors.</param>
    /// <param name="context">The context the model is built for.</param>
    /// <param name="services">The context's application service provider, or <see langword="null"/> at design time.</param>
    public abstract void ConfigureModel(ModelBuilder modelBuilder, DbContext context, IServiceProvider? services);

    /// <summary>Stamps and checks the tenant-owned entities <paramref name="context"/> is about to save.</summary>
    public abstract void SavingChanges(DbContext context);

    /// <summary>Logs a tenant-owned <c>UPDATE</c> or <c>DELETE</c> that matched no row.</summary>
    public abstract void WriteMatchedNoRow(ConcurrencyExceptionEventData eventData);
}

/// <inheritdoc />
internal sealed class TenantIsolation<TKey> : TenantIsolation
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private static readonly EfCoreIsolationOptions DefaultOptions = new();

    public override Type KeyType => typeof(TKey);

    public override bool IsTenantEntity(Type type) => typeof(ITenantEntity<TKey>).IsAssignableFrom(type);

    public override void ConfigureModel(ModelBuilder modelBuilder, DbContext context, IServiceProvider? services)
    {
        ILogger? logger = null;

        if (services is not null)
        {
            // Fail now, naming the missing registration, rather than on the first query.
            ApplicationServices.TenantContext<TKey>(services);
            logger = services.GetService<ILogger<TenantModelCustomizer>>();
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!TenantEntityTypes.IsTenantEntity(entityType.ClrType))
            {
                continue;
            }

            ThrowIfTenantIdIsNotMapped(entityType);
            MarkTenantIdAsConcurrencyToken(entityType);

            // EF Core reads an owned type's rows only through its owner and does not let it have a filter of its
            // own, so the owner's tenant filter must cover it.
            if (entityType.IsOwned())
            {
                TenantEntityTypes.ThrowIfOwnerIsNotTenantEntity(entityType, typeof(TKey));
                continue;
            }

            // EF Core filters an inheritance hierarchy through its root type, so the root's tenant filter covers
            // the derived types.
            if (entityType.BaseType is not null)
            {
                TenantEntityTypes.ThrowIfRootIsNotTenantEntity(entityType, typeof(TKey));
                continue;
            }

            // A shared-type entity type (one of several mapping the same CLR type) is configured by its name.
            var builder = entityType.HasSharedClrType
                ? modelBuilder.SharedTypeEntity(entityType.Name, entityType.ClrType)
                : modelBuilder.Entity(entityType.ClrType);

            AddTenantFilter(builder, entityType, context, logger);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Builds the filter of a model built at run time, which requires dynamic code anyway.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TenantId is a public property of ITenantEntity<TKey>, which the entity type implements.")]
    private static void AddTenantFilter(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder builder,
        IMutableEntityType entityType,
        DbContext context,
        ILogger? logger)
    {
        var tenantFilter = BuildFilter(entityType.ClrType, context);

#if EFCORE10_OR_GREATER
        // EF Core 10 names filters, so the tenant filter can be removed on its own. It does not allow a named filter
        // beside an unnamed one, though, so then the tenant filter joins the unnamed filter.
        var declared = entityType.GetDeclaredQueryFilters();

        // The name is Tenantry's: a filter of the application's by that name would be replaced without a trace.
        if (declared.Any(filter => filter.Key == TenantryQueryFilters.Tenant))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                entityType.ClrType.Name,
                $"Entity '{entityType.ClrType.Name}' has a query filter named '{TenantryQueryFilters.Tenant}', the name of " +
                "the tenant filter Tenantry adds, which would replace it. Give your filter another name.");
        }

        if (declared.FirstOrDefault(filter => filter.Key is null) is not { Expression: { } unnamed })
        {
            builder.HasQueryFilter(TenantryQueryFilters.Tenant, tenantFilter);
            return;
        }

        builder.HasQueryFilter(Combine(unnamed, tenantFilter));
        logger?.LogInformation(
            "Entity '{EntityType}' has an unnamed query filter, so Tenantry merged its tenant filter into it: " +
            "IgnoreQueryFilters([TenantryQueryFilters.Tenant]) cannot remove the tenant filter alone for it. Name the " +
            "entity's filter to keep the two apart",
            entityType.ClrType.Name);
#else
        _ = logger;
        builder.HasQueryFilter(entityType.GetQueryFilter() is { } existing ? Combine(existing, tenantFilter) : tenantFilter);
#endif
    }

    // The filter reads TenantId as a CLR property, and stamping writes it through EF Core, so it must be a mapped
    // property of the key type. One implemented explicitly (ITenantEntity<TKey>.TenantId => OrganizationId) is not.
    private static void ThrowIfTenantIdIsNotMapped(IMutableEntityType entityType)
    {
        if (entityType.FindProperty(TenantOwnership.TenantIdProperty) is not { PropertyInfo: not null } tenantId ||
            tenantId.ClrType != typeof(TKey))
        {
            throw new TenantIsolationViolationException(
                TenantIsolationViolationKind.ModelConfiguration,
                entityType.ClrType.Name,
                $"Tenant-owned entity '{entityType.ClrType.Name}' has no mapped public property 'TenantId' of type " +
                $"{typeof(TKey).Name}, so Tenantry can neither filter nor stamp it. Implement ITenantEntity<{typeof(TKey).Name}>.TenantId " +
                "as a public property (its setter can be private or init-only), and do not ignore it in the model.");
        }
    }

    // Makes EF Core include the stored TenantId in the WHERE clause of every UPDATE and DELETE, so a write can only
    // affect a row that belongs to the tenant the entity was loaded or attached with. A forged TenantId therefore
    // matches no row and SaveChanges fails with DbUpdateConcurrencyException instead of overwriting or deleting another
    // tenant's data. This changes no columns, so no schema migration is needed. Keyless entity types are never
    // updated and are skipped.
    private static void MarkTenantIdAsConcurrencyToken(IMutableEntityType entityType)
    {
        if (!entityType.IsKeyless && entityType.FindProperty(TenantOwnership.TenantIdProperty) is { } tenantId)
        {
            tenantId.IsConcurrencyToken = true;
        }
    }

    // entity => TenantFilter.HasTenant<TKey>(context) && entity.TenantId.Equals(TenantFilter.CurrentTenantId<TKey>(context))
    [RequiresDynamicCode("Builds a lambda for an entity type known only at run time.")]
    [RequiresUnreferencedCode("Reads TenantId by name.")]
    private static LambdaExpression BuildFilter(Type entityClrType, DbContext context)
    {
        Expression<Func<DbContext, TKey, bool>> template = (dbContext, tenantId) =>
            TenantFilter.HasTenant<TKey>(dbContext) && tenantId.Equals(TenantFilter.CurrentTenantId<TKey>(dbContext));

        var entity = Expression.Parameter(entityClrType, "entity");

        // A DbContext-typed constant: EF Core puts the context running the query in its place.
        var body = ParameterReplacer.Replace(template.Body, template.Parameters[0], Expression.Constant(context, typeof(DbContext)));
        body = ParameterReplacer.Replace(body, template.Parameters[1], Expression.Property(entity, TenantOwnership.TenantIdProperty));

        return Expression.Lambda(body, entity);
    }

    [RequiresDynamicCode("Builds a lambda for an entity type known only at run time.")]
    private static LambdaExpression Combine(LambdaExpression existing, LambdaExpression tenant) =>
        Expression.Lambda(
            Expression.AndAlso(existing.Body, ParameterReplacer.Replace(tenant.Body, tenant.Parameters[0], existing.Parameters[0])),
            existing.Parameters);

    public override void SavingChanges(DbContext context)
    {
        var services = ApplicationServices.Find(context);
        var tenantContext = ApplicationServices.TenantContext<TKey>(context);
        var logger = (ILogger?)services?.GetService<ILogger<TenantSaveChangesInterceptor>>() ?? NullLogger.Instance;

        if (!tenantContext.HasTenant)
        {
            HandleMissingTenant(context, services?.GetService<EfCoreIsolationOptions>() ?? DefaultOptions, logger);
            return;
        }

        TenantWriteIsolationApplier.Apply(context.ChangeTracker.Entries(), tenantContext, violation =>
        {
            logger.LogError(
                "Tenant isolation violation: entity '{EntityType}' belongs to tenant '{OffendingTenantId}' " +
                "but the current tenant is '{ExpectedTenantId}'. Aborting SaveChanges",
                violation.TypeName,
                violation.OffendingTenantId,
                violation.ExpectedTenantId);
        });
    }

    private static void HandleMissingTenant(DbContext context, EfCoreIsolationOptions options, ILogger logger)
    {
        var scopedWrites = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is ITenantEntity<TKey> &&
                            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        // A save that writes no tenant-owned entity (e.g. a host-level catalogue) needs no tenant.
        if (scopedWrites.Count == 0)
        {
            return;
        }

        var entityTypes = string.Join(", ", scopedWrites.Select(entry => entry.Metadata.ClrType.Name).Distinct());

        switch (options.OnMissingTenant)
        {
            case MissingTenantBehavior.Warn:
                logger.LogWarning(
                    "SaveChanges is writing tenant-scoped entities ({EntityTypes}) without a resolved tenant. " +
                    "Updates and deletes are not tenant-checked (EfCoreIsolationOptions.OnMissingTenant = Warn)",
                    entityTypes);
                break;

            case MissingTenantBehavior.Allow:
                break;

            default:
                throw new TenantNotResolvedException(
                    $"SaveChanges is writing tenant-scoped entities ({entityTypes}) without a resolved tenant. " +
                    "Run the write while a tenant is current (app.UseTenantry() for requests, " +
                    "ITenantScopeFactory.RunInScopeAsync or CreateScope elsewhere), or set " +
                    "EfCoreIsolationOptions.OnMissingTenant to Allow or Warn for maintenance code that deliberately " +
                    "writes across tenants.");
        }

        // Even when unscoped writes are allowed, a new row must name its tenant: an unowned row is never
        // visible through the tenant filter and belongs to no one.
        var unowned = scopedWrites.FirstOrDefault(entry =>
            entry.State == EntityState.Added &&
            TenantOwnership.IsUnstamped(((ITenantEntity<TKey>)entry.Entity).TenantId));

        if (unowned is not null)
        {
            throw new TenantNotResolvedException(
                $"A new '{unowned.Metadata.ClrType.Name}' is being saved without a resolved tenant and without a " +
                "TenantId. Set TenantId explicitly or save it while its tenant is current.");
        }
    }

    // A tenant-owned UPDATE or DELETE that affects no row is either an ordinary concurrency conflict or an attempt to
    // write another tenant's row with a forged TenantId. The two cannot be told apart without another query, so EF
    // Core's DbUpdateConcurrencyException is left as it is and logged here. Logging never replaces that exception.
    public override void WriteMatchedNoRow(ConcurrencyExceptionEventData eventData)
    {
        if (eventData.Context is not { } context ||
            ApplicationServices.Find(context) is not { } services ||
            services.GetService<ILogger<TenantSaveChangesInterceptor>>() is not { } logger)
        {
            return;
        }

        var tenantContext = services.GetService<ITenantContext<TKey>>();
        var tenantId = tenantContext is { HasTenant: true } ? tenantContext.CurrentTenantId?.ToString() : null;

        foreach (var entry in eventData.Entries)
        {
            if (entry.Entity is ITenantEntity<TKey>)
            {
                logger.LogWarning(
                    "A {State} of tenant-scoped entity '{EntityType}' in tenant '{TenantId}' matched no row. " +
                    "The row does not exist, belongs to another tenant, or was changed concurrently",
                    entry.State,
                    entry.Entity.GetType().Name,
                    tenantId);
            }
        }
    }

    private sealed class ParameterReplacer(ParameterExpression source, Expression replacement) : ExpressionVisitor
    {
        public static Expression Replace(Expression expression, ParameterExpression source, Expression replacement) =>
            new ParameterReplacer(source, replacement).Visit(expression);

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? replacement : base.VisitParameter(node);
    }
}
