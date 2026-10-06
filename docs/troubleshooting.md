# Troubleshooting

Most isolation surprises come down to no tenant in scope, the wrong `TKey`, middleware order, or a context without
`UseTenantry()`.

## Startup fails with "has no tenant resolvers", "has no tenant store" or "found no tenant resolution"

`app.UseTenantry()` checks the registration when the pipeline is built. Add at least one resolver
(`ResolveFromHeader(...)`, `ResolveFromClaim(...)`, `UseResolver(...)`) and exactly one store (`UseInMemoryStore(...)`
or `UseStore<T>()`) to `AddTenantry`. In a worker or console app, `ITenantLookup` and
`ITenantScopeFactory.RunInScopeAsync` throw the same "has no tenant store" error, and a hosted service that depends on
the lookup throws it as the host starts.

## Startup fails with "app.UseTenantry() is not in the request pipeline"

You registered request resolution (a `ResolveFrom…` or `UseResolver` method) but never called `app.UseTenantry()`, so
no request would have a tenant. Add it where [Pipeline ordering](aspnetcore-integration.md#pipeline-ordering) says.

## Startup fails with "app.UseTenantResolution() is in the request pipeline but app.UseTenantry() is not"

The access validators run in `app.UseTenantry()`, so without it no tenant is checked. Add `app.UseTenantry()` after
`app.UseAuthentication()`. See [Authentication per tenant](authentication-per-tenant.md).

## Startup fails with "app.UseAuthorization() is between app.UseTenantResolution() and app.UseTenantry()"

Authorization there would run on the tenant the request names, before the access validators check it, so a policy
that reads the tenant could let in a caller who may not use it. Call `app.UseAuthorization()` after
`app.UseTenantry()`. See [Authentication per tenant](authentication-per-tenant.md).

## Registration fails with "A tenant store is already registered" or "already registered with tenant key type"

An application has one store and one tenant key type. Remove the second `UseStore`/`UseInMemoryStore` (or the
`ITenantStore<TKey>` you registered yourself), and call `AddTenantry` with the same key type everywhere.

## Queries return no rows for a valid tenant

With no tenant current, the query filter matches nothing. Check:

1. No tenant is in scope: `ITenantContext<TKey>.HasTenant` must be true at the query. On the web, `UseTenantry()` must
   have run and resolved one; in a worker, you must be inside an `ITenantScopeFactory` scope (or
   `ITenantContextSetter.MakeCurrent`).
2. The request named no tenant: a missing or blank header (or other source) means none. Add `.RequireTenant()` to get
   a `400` instead of empty results.
3. The entity uses another `TKey`: a `Guid` registration and an `ITenantEntity<string>` entity never match.
4. The tenant id is the default value: `Guid.Empty`, `0` and an empty string mean "no tenant", and making such a
   tenant current throws `ArgumentException`.

## Queries return all tenants' rows

- `IgnoreQueryFilters()` was called, directly or in a shared queryable helper.
- The entity does not implement `ITenantEntity<TKey>` (or derive from `TenantEntity<TKey>`), so it is global. To have
  such types reported, set `OnUnmarkedEntityType`
  ([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).
- The context's options do not call `UseTenantry()`. Add it where the context is registered.

## `TenantId` is not stamped on insert

- The context's options do not call `UseTenantry()`.
- No tenant is in scope at `SaveChanges`. By default the save throws `TenantNotResolvedException` and writes nothing;
  under `Warn` and `Allow` a new entity must still carry its `TenantId`, or the save throws. Save inside a tenant
  scope, or set `TenantId` yourself in deliberate cross-tenant code. See
  [`OnMissingTenant`](efcore-integration.md#onmissingtenant-writes-with-no-tenant).

## `TenantIsolationViolationException` on save

Isolation is working: a modified or deleted entity, or an added one that names another tenant, belongs to a tenant
other than the current one. `Kind` is `EntityWrite`; `OffendingTenantId` and `ExpectedTenantId` name the tenants.

- You loaded the entity in one tenant's scope and changed it in another's. Work inside the owning tenant's scope.
- You set `TenantId` to another tenant. Leave it to the interceptor.
- For admin work across tenants, use a fresh `DbContext` in each tenant's scope, or `IgnoreQueryFilters()` for reads.
  `SaveChanges` still checks every write against the current tenant, but `IgnoreQueryFilters()` with
  `ExecuteUpdate`/`ExecuteDelete` affects every tenant.

"No row with its key is stored for the current tenant" comes from reading the stored row of an owner, or of an entity
over more than one table, before the save: the row is another tenant's, or gone. The read ignores your own query
filters, so a soft-deleted row of the current tenant still passes ([Advanced](efcore-advanced.md)).

## `Commit` throws `TenantIsolationViolationException`: "rolled back, not committed"

`Kind` is `TransactionRolledBack`. A save in this transaction failed partway, and a save in it wrote rows (owned rows in
their own table, an entity split across tables, or many-to-many join rows) that depend on another statement's tenant
check. EF Core had no savepoint to undo just the failed save (for example SQL Server with MARS), or could not roll back
to it, so Tenantry rolled back the whole transaction. In a `TransactionScope`, disposing the scope throws
`TransactionAbortedException` instead.

Any failure of any save counts, and a save that never reported success, so catching a failed save and going on in the
same transaction is refused at the commit. Run the unit of work again in a new transaction, or use one with savepoints
([Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole) has the rules and
how).

With Npgsql, a transaction can also be refused for an earlier one. If a context began a transaction through EF Core,
a save in it failed, and the transaction was disposed without an EF Core commit or rollback while the context stayed
in use, a transaction later begun through ADO.NET on the same connection can get the same `NpgsqlTransaction` object,
and is refused when handed to a context with `UseTransaction`. Begin that transaction through EF Core
(`Database.BeginTransaction`) instead, or end the failed one through EF Core with `RollbackTransaction`.

## `TenantIsolationViolationException` of kind `SaveWithoutTransaction`

`Database.AutoTransactionBehavior` is `Never`, `OnSaveWithoutTransaction` is `Reject`, and the save has rows that
depend on another statement's tenant check (owned rows in their own table, an entity split across tables, or the join
rows of a many-to-many relationship). Without a transaction, a failed check could leave the other rows written, so
nothing was sent. Save in a transaction, or set `OnSaveWithoutTransaction` back to its default, `UseTransaction`. See
[Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole).

## `TenantIsolationViolationException`: "has no tenant query filter" or "is not a concurrency token"

A tenant-owned entity type lost its tenant filter or concurrency token after `UseTenantry()` added them, for example
to a model-building convention, or the model is a compiled model (`UseModel`), which Tenantry does not support. The
query or save is stopped before it runs.

## `TenantIsolationViolationException`: "requires every entity type that is not tenant-owned to be marked as shared"

The application set `OnUnmarkedEntityType` to `Reject`, and the entity types the message names are neither tenant-owned
nor marked. Implement `ITenantEntity<TKey>` on each whose rows belong to a tenant, and mark each that every tenant
shares with `[SharedAcrossTenants]` or `IsSharedAcrossTenants()`. See
[Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned).

## `InvalidOperationException`: "UseTenantry() was called before its options had the application's services"

The options call `UseTenantry()` before `UseApplicationServiceProvider`, and the application sets
`OnUnmarkedEntityType` to `Warn` or `Reject`. Call `UseApplicationServiceProvider` first, or set the option on the
context with `UseTenantry(o => …)`. `AddDbContext` and its relatives set the services first.

## The model fails to build with `TenantIsolationViolationException` or "Tenantry is not registered"

`UseTenantry()` refuses models it cannot isolate, usually on the first query
([the full list, with reasons](efcore-advanced.md#models-that-cannot-be-isolated)). The usual fixes:

- "implements ITenantEntity&lt;X&gt;, but '…' implements ITenantEntity&lt;Y&gt;": use one key type, the one you
  register.
- "Tenantry is not registered for 'X' tenant keys": call `AddTenantry<X>` in the context's service collection.
- "base entity type … is not" or "owner … is not": make the root type or the owner tenant-owned too.
- "has no TenantId and a key that does not include its owner's key": remove the `HasKey`, or implement
  `ITenantEntity<TKey>` on the owned type.
- "shares table … but is not tenant-owned" (first query or save): make the sharing type tenant-owned, or give it its
  own table.
- "owned through a key of … that neither includes nor is part of its primary key": remove `HasPrincipalKey`, or own
  it through a key that includes `TenantId`.
- "has no mapped public property 'TenantId'": make `TenantId` a public property of the key type (its setter can be
  private or init-only).
- "has a query filter named 'Tenantry.Tenant'" (EF Core 10): rename your filter.
- "The join entity … has a key that does not include its foreign key": remove the join entity's own key, or implement
  `ITenantEntity<TKey>` on it. "The join entity … names … through a key": join the end through its primary key, or a
  key that includes `TenantId`. See [Many-to-many relationships](efcore-advanced.md#many-to-many-relationships).
- "is tenant-owned and mapped to JSON": remove `ITenantEntity<TKey>` from the owned type; its owner isolates it.

## Creating the context fails with "replaces EF Core's IModelCustomizer" or "UseInternalServiceProvider"

`UseTenantry()` adds the tenant filters through its own model customizer.

- "This context replaces EF Core's IModelCustomizer": move the `ReplaceService<IModelCustomizer, …>()` configuration
  into `OnModelCreating` or an `ITenantModelContributor`.
- "cannot be used with UseInternalServiceProvider" (on EF Core 10, possibly EF Core's own error about
  `ISingletonInterceptor` services): remove `UseInternalServiceProvider`.

See [Extending](efcore-integration.md#extending-contributors).

## `Reload()` detaches an entity, or `GetDatabaseValues()` returns `null`

Tenantry keeps the tenant filter on EF Core's read by key, so another tenant's row, or any row with no tenant current,
reads as deleted, and on EF Core 8 and 9 so does a soft-deleted row
([What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated)). Read a soft-deleted row with
`IgnoreQueryFilters()` and your own key and `TenantId` predicate, and in a `DbUpdateConcurrencyException` handler treat
`null` as "not found".

## "has no application service provider"

The context was built without the application's services, so Tenantry cannot find the current tenant. Register it with
`AddDbContext` (or a pool or factory), or call `UseApplicationServiceProvider` on its options. Design-time tools can
build the model without them.

## Filter uses a stale tenant or leaks across requests

`UseTenantry()`'s filter reads the tenant on every query, so it cannot go stale. A filter of your own that captures a
tenant id or an `ITenantContext<TKey>` in `OnModelCreating` is compiled once and reused: read the tenant through the
context, or leave it to `UseTenantry()`. See
[How the query filter stays correct](efcore-integration.md#how-the-query-filter-stays-correct).

## An endpoint returns `500` after `UseTenantResolution()` (event 1011)

The request reached its endpoint without passing `app.UseTenantry()`, so its tenant was never checked and the endpoint
did not run. Call `app.UseTenantry()` in every branch of the pipeline, where
[Pipeline ordering](aspnetcore-integration.md#pipeline-ordering) says.

## Every request returns `500` after `UseTenantResolution()` (event 1013)

The authorization middleware, added some other way than `app.UseAuthorization()` (which would have failed the start),
runs between `app.UseTenantResolution()` and `app.UseTenantry()`, where it would authorize on a tenant the access
validators have not checked, so the request is refused. Move it after `app.UseTenantry()`.

## Authentication ignores the tenant's settings (event 1010)

The authentication middleware ran before `app.UseTenantResolution()`, so it used no tenant's settings. Call
`app.UseAuthentication()` yourself, after `app.UseTenantResolution()`: the one `WebApplication` adds on its own runs
first. A tenant resolved after authentication, by a claim resolver or one added after it, always uses the defaults:
add the resolver for authentication before any claim resolver. See
[Authentication per tenant](authentication-per-tenant.md).

## A request with the tenant header has no tenant

A header or query parameter sent more than once names no tenant, so a proxy must replace the client's header, not
add another. See [Header](tenant-resolution.md#header).

## A query throws "reads its tenant's connection string asynchronously"

The connection strings can only be read asynchronously (only `GetConnectionStringAsync` is set), so a context from
`AddDbContextPerTenantDatabase` reads its string when it first opens a connection, and a synchronous call cannot.
Use the asynchronous EF Core methods, or also set `GetConnectionString`. See
[Database per tenant](efcore-integration.md#database-per-tenant).

## Claim-based resolution or validation never matches

- `UseTenantry()` runs before `UseAuthentication()`, so `HttpContext.User` is empty (see
  [Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). For `ResolveFromClaim`, the middleware logs this
  once (event 1008, see [Diagnostics](diagnostics.md#logs)).
- The endpoint uses a non-default scheme (`[Authorize(AuthenticationSchemes = …)]`), whose user is signed in by
  authorization, after `UseTenantry()`. Make that scheme the default.
- The claim type does not match (`ResolveFromClaim("tenant_id")` vs. the actual claim name).
- The claim value names no tenant: `ValidateTenantAccessByClaim` compares tenant ids, and a resolved claim is looked up
  like any identifier (a non-GUID string for a `Guid` key names no tenant).

## `AllowMissingTenant()` has no effect

The middleware ran before routing chose the endpoint, so `RequireTenantByDefault()` applied. It logs this once (event
1007) for the first request it lets through, such as one with a tenant, not for one it rejects. See
[Pipeline ordering](aspnetcore-integration.md#pipeline-ordering).

## Route-value resolution returns null

Routing ran after the middleware, so the route value did not exist yet. Unless `RequireTenantByDefault()` rejected
the request first, the middleware logs this once (event 1007). See
[Pipeline ordering](aspnetcore-integration.md#pipeline-ordering). With `app.UseTenantResolution()`, call
`app.UseRouting()` before it as well, or authentication runs without the route's tenant (event 1016, logged once).

## Subdomain resolution returns null on localhost

Without a base domain, `ResolveFromSubdomain` needs at least three host segments, so `localhost` and `acme.localhost`
resolve to `null`. For development, `tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("localhost"))` resolves
`acme.localhost` to `acme`.

## Subdomains resolve, but name no tenant (`404`)

With `Guid` or `int` keys, `acme` is not a tenant id: the default `FindByIdentifierAsync` parses the identifier as the
key type. Implement `FindByIdentifierAsync` in your store to map slugs. See
[Identifiers other than the id](tenant-resolution.md#identifiers-other-than-the-id).

## A suspended tenant is still served

With `CacheTenants`, the cached tenant is served until its entry expires: call
`ITenantInvalidator<TKey>.InvalidateAsync` when you change a tenant ([Caching](tenant-stores.md#caching)). If its
background work still runs, refuse it with `ValidateTenantActivity` rather than an access validator, which only HTTP
requests run ([Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).

## Requests have no `tenant.id` tag or `TenantId` log scope

The tag is on the request's trace span, which exists only with tracing (OpenTelemetry's ASP.NET Core instrumentation,
for example). The scope needs a logging provider that records scopes (`IncludeScopes`). Both are added only to
requests that resolve a tenant. See [Diagnostics](diagnostics.md).

## Queued work, or the caller of a helper that opened a scope, has no tenant

The `AsyncLocal` tenant flows into awaited work, but not past the scope's disposal, and not back from an `async` helper
to its caller ([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)). For queued work, capture the tenant id
when enqueuing and run the work with `ITenantScopeFactory.RunInScopeAsync(id, …)`
([Work that runs later](non-http-hosts.md#work-that-runs-later)). Otherwise open the scope in the method that does the
work (`await using var scope = scopes.CreateScope(tenant);`), or pass the work in with `RunInScopeAsync`.

## AOT or trim warnings from the EF Core integration

These are expected: EF Core's `DbContext` is annotated `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`, and
`UseTenantry()` builds its query filters as expression trees. EF Core is not AOT-compatible, so do not publish an EF
Core app with Native AOT. See [AOT & trimming](aot-and-trimming.md).
