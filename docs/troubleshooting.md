# Troubleshooting

Most isolation surprises come down to no tenant in scope, the wrong `TKey`, middleware order, or a context without
`UseTenantry()`.

## Startup fails with "has no tenant resolvers", "has no tenant store" or "found no tenant resolution"

Add at least one resolver (`ResolveFromHeader(...)`, `ResolveFromClaim(...)`, `UseResolver(...)`) and exactly one store
(`UseInMemoryStore(...)` or `UseStore<T>()`) to `AddTenantry`. `app.UseTenantry()` checks the registration when the
pipeline is built. In a worker or console app, `ITenantLookup` and `ITenantScopeFactory.RunInScopeAsync` throw the same
"has no tenant store" error. A hosted service that depends on the lookup throws it as the host starts.

## Startup fails with "app.UseTenantry() is not in the request pipeline"

Add `app.UseTenantry()` where [Pipeline ordering](aspnetcore-integration.md#pipeline-ordering) says. You registered
request resolution (a `ResolveFrom…` or `UseResolver` method) without it, so no request would have a tenant.

## Startup fails with "app.UseTenantResolution() is in the request pipeline but app.UseTenantry() is not"

Add `app.UseTenantry()` after `app.UseAuthentication()`. The access validators run in it, so without it no tenant is
checked ([Authentication per tenant](authentication-per-tenant.md)).

## Startup fails with "app.UseAuthorization() is between app.UseTenantResolution() and app.UseTenantry()"

Call `app.UseAuthorization()` after `app.UseTenantry()`. Between the two, authorization runs before the access
validators check the tenant ([Checks on the pipeline](authentication-per-tenant.md#checks-on-the-pipeline)).

## Registration fails with "A tenant store is already registered" or "already registered with tenant key type"

An application has one store and one tenant key type. Remove the second `UseStore`/`UseInMemoryStore`, or the
`ITenantStore<TKey>` you registered yourself. Call `AddTenantry` with the same key type everywhere.

## Queries return no rows for a valid tenant

With no tenant current, the query filter matches nothing. Check:

1. A tenant is in scope: `ITenantContext<TKey>.HasTenant` must be true at the query. On the web, `UseTenantry()` must
   have run and resolved one. In a worker, the query must run inside an `ITenantScopeFactory` scope (or after
   `ITenantContextSetter.MakeCurrent`).
2. The request names a tenant. A missing or blank header (or other source) names none. Add `.RequireTenant()` to get a
   `400` instead of empty results.
3. The tenant id is not the key type's default. `Guid.Empty`, `0` and an empty string mean "no tenant", and making
   such a tenant current throws `ArgumentException`.

An entity with another `TKey` than the one registered fails the model instead
([below](#the-model-fails-to-build-with-tenantisolationviolationexception-or-tenantry-is-not-registered)).

## Queries return all tenants' rows

- `IgnoreQueryFilters()` was called, directly or in a shared queryable helper.
- The entity does not implement `ITenantEntity<TKey>` (or derive from `TenantEntity<TKey>`), so it is global. To have
  such types reported, set `OnUnmarkedEntityType`
  ([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).
- The context's options do not call `UseTenantry()`. Add it where the context is registered.

## `TenantId` is not stamped on insert

Save inside a tenant scope, through a context whose options call `UseTenantry()`. With no tenant in scope, the save
throws `TenantNotResolvedException` by default and writes nothing. Under `Warn` and `Allow` a new entity must still
carry its `TenantId`, or the save throws, so set it yourself in deliberate cross-tenant code
([`OnMissingTenant`](efcore-integration.md#onmissingtenant-writes-with-no-tenant)).

## `TenantIsolationViolationException` on save

Isolation is working: a modified or deleted entity, or an added one that names another tenant, belongs to a tenant
other than the current one. `Kind` is `EntityWrite`, `ExpectedTenantId` is the current tenant, and `OffendingTenantId`
is the entity's tenant, or `null` when Tenantry could not read it.

- You loaded the entity in one tenant's scope and changed it in another's. Work inside the owning tenant's scope.
- You set `TenantId` to another tenant. Leave it to the interceptor.
- For admin work across tenants, use a fresh `DbContext` in each tenant's scope, or `IgnoreQueryFilters()` for reads.
  `SaveChanges` still checks every write against the current tenant. `IgnoreQueryFilters()` with
  `ExecuteUpdate`/`ExecuteDelete` affects every tenant.

The message "no row with its key is stored for the current tenant" means the stored row of an owner, or of an entity
over more than one table, is another tenant's or gone. Tenantry reads that row before the save. The read ignores your
own query filters, so a soft-deleted row of the current tenant still passes ([Advanced](efcore-advanced.md)).

## `Commit` throws `TenantIsolationViolationException`: "rolled back, not committed"

Run the unit of work again in a new transaction, or use a transaction with savepoints
([Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole) has the rules and
how). Catching a failed save and going on in the same transaction is refused at the commit.

`Kind` is `TransactionRolledBack`. A save in this transaction wrote rows checked through another statement (owned rows
in their own table, an entity split across tables, or many-to-many join rows), and a save that sent statements in it
failed or never reported success. EF Core could not undo only the failed save (no savepoint, as with SQL Server's MARS,
or a failed rollback to its savepoint), so Tenantry rolled back the whole transaction. In a `TransactionScope`,
disposing the scope throws `TransactionAbortedException` instead.

With Npgsql, a transaction can also be
[refused for an earlier one](efcore-advanced.md#npgsql-a-transaction-refused-for-an-earlier-one).

## `TenantIsolationViolationException` of kind `SaveWithoutTransaction`

Save in a transaction, or set `OnSaveWithoutTransaction` back to its default, `UseTransaction`. The exception means
`Database.AutoTransactionBehavior` is `Never` and `OnSaveWithoutTransaction` is `Reject`. Then a save with rows checked
through another statement (owned rows in their own table, an entity split across tables, or many-to-many join rows)
sends nothing. Without a transaction, a failed check could leave the other rows written
([Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole)).

## `TenantIsolationViolationException`: "has no tenant query filter" or "is not a concurrency token"

A tenant-owned entity type lost its tenant filter or concurrency token after `UseTenantry()` added them, for example to
a model-building convention. Or the model is a compiled model (`UseModel`), which Tenantry does not support. The query
or save is stopped before it runs.

## `TenantIsolationViolationException`: "requires every entity type that is not tenant-owned to be marked as shared"

Implement `ITenantEntity<TKey>` on each type the message names whose rows belong to a tenant. Mark each that every
tenant shares with `[SharedAcrossTenants]` or `IsSharedAcrossTenants()`. The application set `OnUnmarkedEntityType` to
`Reject`, and these types are neither tenant-owned nor marked
([Entity types that are not tenant-owned](efcore-integration.md#entity-types-that-are-not-tenant-owned)).

## `InvalidOperationException`: "UseTenantry() was called before its options had the application's services"

Call `UseApplicationServiceProvider` before `UseTenantry()`, or set the option on the context with
`UseTenantry(o => …)`. The error comes when the application sets `OnUnmarkedEntityType` to `Warn` or `Reject`.
`AddDbContext` and its relatives set the services first.

## The model fails to build with `TenantIsolationViolationException` or "Tenantry is not registered"

`UseTenantry()` refuses models it cannot isolate, usually on the first query
([the full list, with reasons](efcore-advanced.md#models-that-cannot-be-isolated)). The fix for each message:

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
- "has no mapped public property 'TenantId'": make `TenantId` a public property of the key type. Its setter can be
  private or init-only.
- "has a query filter named 'Tenantry.Tenant'" (EF Core 10): rename your filter.
- "The join entity … has a key that does not include its foreign key": remove the join entity's own key, or implement
  `ITenantEntity<TKey>` on it.
- "The join entity … names … through a key": join the end through its primary key, or a key that includes `TenantId`
  ([Many-to-many relationships](efcore-advanced.md#many-to-many-relationships)).
- "is tenant-owned and mapped to JSON": remove `ITenantEntity<TKey>` from the owned type. Its owner isolates it.

## Creating the context fails with "replaces EF Core's IModelCustomizer" or "UseInternalServiceProvider"

`UseTenantry()` adds the tenant filters through its own model customizer
([`UseTenantry`](api/microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md)).

- "This context replaces EF Core's IModelCustomizer": move the `ReplaceService<IModelCustomizer, …>()` configuration
  into `OnModelCreating` or an `ITenantModelContributor` ([Extending](efcore-integration.md#extending-contributors)).
- "cannot be used with UseInternalServiceProvider": remove `UseInternalServiceProvider`. On EF Core 10 the error can
  be EF Core's own, about `ISingletonInterceptor` services.

## `Reload()` detaches an entity, or `GetDatabaseValues()` returns `null`

Treat `null` as "not found" in a `DbUpdateConcurrencyException` handler. Read a soft-deleted row with
`IgnoreQueryFilters()` and your own key and `TenantId` predicate. Tenantry keeps the tenant filter on EF Core's read by
key, so another tenant's row, or any row with no tenant current, reads as deleted. On EF Core 8 and 9 so does a
soft-deleted row ([What is and isn't isolated](efcore-integration.md#what-is-and-isnt-isolated)).

## "has no application service provider"

Register the context with `AddDbContext` (or a pool or factory), or call `UseApplicationServiceProvider` on its
options. It was built without the application's services, so Tenantry cannot find the current tenant. Design-time
tools can build the model without them.

## Filter uses a stale tenant or leaks across requests

`UseTenantry()`'s filter reads the tenant on every query, so it cannot go stale. A filter of your own that captures a
tenant id or an `ITenantContext<TKey>` in `OnModelCreating` is compiled once and reused. Read the tenant through the
context, or leave it to `UseTenantry()`
([How the query filter stays correct](efcore-integration.md#how-the-query-filter-stays-correct)).

## An endpoint returns `500` after `UseTenantResolution()` (event 1011)

Call `app.UseTenantry()` in every branch of the pipeline, where
[Pipeline ordering](aspnetcore-integration.md#pipeline-ordering) says. The request reached its endpoint without
passing `app.UseTenantry()`, so its tenant was never checked and the endpoint did not run.

## Every request returns `500` after `UseTenantResolution()` (event 1013)

Move the authorization middleware after `app.UseTenantry()`. It was added between `app.UseTenantResolution()` and
`app.UseTenantry()` some other way than `app.UseAuthorization()`, which would have failed the start. There it would
authorize on a tenant the access validators have not checked, so the request is refused
([Checks on the pipeline](authentication-per-tenant.md#checks-on-the-pipeline)).

## Authentication ignores the tenant's settings (event 1010)

Call `app.UseAuthentication()` yourself, after `app.UseTenantResolution()`. The authentication middleware ran before
it, so it used no tenant's settings: the one `WebApplication` adds on its own runs first. A tenant resolved after
authentication, by a claim resolver or one added after it, always uses the defaults. So add the resolver for
authentication before any claim resolver ([Authentication per tenant](authentication-per-tenant.md)).

## A request with the tenant header has no tenant

A header or query parameter sent more than once names no tenant. A proxy must replace the client's header, not add
another ([Header](tenant-resolution.md#header)).

## A query throws "reads its tenant's connection string asynchronously"

Use the asynchronous EF Core methods, or also set `GetConnectionString`. With only `GetConnectionStringAsync` set, a
context from `AddDbContextPerTenantDatabase` reads its connection string when it first opens a connection, and a
synchronous call cannot ([Database per tenant](efcore-integration.md#database-per-tenant)).

## Claim-based resolution or validation never matches

- `UseTenantry()` runs before `UseAuthentication()`, so `HttpContext.User` is empty
  ([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). For `ResolveFromClaim`, the middleware logs this
  once ([event 1008](diagnostics.md#logs)).
- The endpoint uses a non-default scheme (`[Authorize(AuthenticationSchemes = …)]`), whose user is signed in by
  authorization, after `UseTenantry()`. Make that scheme the default.
- The claim type does not match (`ResolveFromClaim("tenant_id")` vs. the actual claim name).
- The claim value names no tenant. `ValidateTenantAccessByClaim` compares tenant ids. A resolved claim is looked up like
  any identifier, so a non-GUID string for a `Guid` key names no tenant.

## `AllowMissingTenant()` has no effect

The middleware ran before routing chose the endpoint, so `RequireTenantByDefault()` applied
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). It logs this once (event 1007), for the first
request it lets through, such as one with a tenant, not for one it rejects.

## Route-value resolution returns null

Routing ran after the middleware, so the route value did not exist yet
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). The middleware logs this once (event 1007), unless
`RequireTenantByDefault()` rejected the request first. With `app.UseTenantResolution()`, call `app.UseRouting()` before
it as well. Otherwise authentication runs without the route's tenant (event 1016, logged once).

## Subdomain resolution returns null on localhost

Add `localhost` as a base domain for development: `tenant.ResolveFromSubdomain(o => o.BaseDomains.Add("localhost"))`
resolves `acme.localhost` to `acme`. Without a base domain, `ResolveFromSubdomain` needs at least three host segments,
so `localhost` and `acme.localhost` resolve to `null`.

## Subdomains resolve, but name no tenant (`404`)

Implement `FindByIdentifierAsync` in your store to map slugs
([Identifiers other than the id](tenant-resolution.md#identifiers-other-than-the-id)). With `Guid` or `int` keys,
`acme` is not a tenant id: the default `FindByIdentifierAsync` parses the identifier as the key type.

## A suspended tenant is still served

Call `ITenantInvalidator<TKey>.InvalidateAsync` when you change a tenant. With `CacheTenants`, the cached tenant is
served until its entry expires ([Caching](tenant-stores.md#caching)). If its background work still runs, refuse it with
`ValidateTenantActivity`, not an access validator: only HTTP requests run access validators
([Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).

## Requests have no `tenant.id` tag or `TenantId` log scope

The tag is on the request's trace span, which exists only with tracing (OpenTelemetry's ASP.NET Core instrumentation,
for example). The scope needs a logging provider that records scopes (`IncludeScopes`). Both are added only to
requests that resolve a tenant ([Diagnostics](diagnostics.md)).

## Queued work, or the caller of a helper that opened a scope, has no tenant

For queued work, capture the tenant id when enqueuing, and run the work with
`ITenantScopeFactory.RunInScopeAsync(id, …)` ([Work that runs later](non-http-hosts.md#work-that-runs-later)).
Otherwise open the scope in the method that does the work (`await using var scope = scopes.CreateScope(tenant);`), or
pass the work in with `RunInScopeAsync`. The `AsyncLocal` tenant flows into awaited work, but not past the scope's
disposal, and not back from an `async` helper to its caller
([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)).

## AOT or trim warnings from the EF Core integration

These are expected. Do not publish an EF Core app with Native AOT
([`Tenantry.EfCore`: trimmable, not AOT-compatible](aot-and-trimming.md#tenantryefcore-trimmable-not-aot-compatible)).
