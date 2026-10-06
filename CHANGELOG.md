# Changelog

All notable changes to Tenantry will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Upgrading from 0.7

- Tenantry.Options' warning `OrdinaryOptionsReadAsTenant` is now event 2008, where it was 3001, so Tenantry's events
  are 1000 to 2999 and Tenantry.Pro's are 3000 and above. Change a log alert or filter on event 3001 in the category
  `Tenantry.Options` to 2008. `TenantryWarnings.OrdinaryOptionsReadAsTenant` is 2008, so an `IgnoreWarnings` call that
  names it needs no change. Tenantry.Pro 0.8 uses this numbering.
- `TenantInactiveException.TenantId` holds the tenant's id as the application's key type, as
  `TenantNotFoundException.TenantId` does. It held the id as text, so with a `Guid` or `int` key, cast it to that type
  rather than to `string`.
- An `ITenantActivityValidator<TKey>` registered as scoped or transient makes `ITenantActivity<TKey>` throw
  `InvalidOperationException` when it is first resolved, which `RunInScopeAsync` and the request middleware do. Such a
  validator was resolved once, from the root provider, and shared by every request. Register it as a singleton with
  `tenant.ValidateTenantActivity<TValidator>()`, and have it create a scope for any scoped service it needs.

### Added

- `Tenantry.Templates`, `dotnet new` templates for applications that use Tenantry, published with each release.
  Install them with `dotnet new install Tenantry.Templates`, then create a project with
  `dotnet new tenantry-api -n Orders.Api`, an ASP.NET Core API with EF Core that takes the tenant from the
  `X-Tenant-Id` header and checks it against the caller's JWT `tenant_id` claims, or
  `dotnet new tenantry-worker -n Orders.Worker`, a worker service with EF Core that runs each message as the tenant it
  names with `RunInScopeAsync`. The projects reference the Tenantry packages of the templates' version;
  `--TenantryVersion` picks another. They target `net10.0`, so building one needs the .NET 10 SDK, though an older
  SDK can install the templates and create a project.
- TNY1004, a warning in `Tenantry.EfCore`: a context with tenant-owned entities (a `DbSet<T>` or
  `modelBuilder.Entity<T>()` of a tenant-owned type) registered with `AddDbContext`, `AddDbContextPool`,
  `AddDbContextFactory` or `AddPooledDbContextFactory` whose options do not call `UseTenantry()`, so none of its
  tenant-owned entities is isolated. A build that treats warnings as errors fails on it. It reports nothing where the
  options hand the builder to code that could call `UseTenantry()`, an `OnConfiguring` of the context could, or
  another registration or `ConfigureDbContext` of the context in the same project calls it (on EF Core 8, an earlier
  registration or one in another method); and, on EF Core 9 and later, for a context declared in another project,
  whose own registrations it cannot see. See [TNY1004](docs/analyzers.md#tny1004).
- `tenant.IgnoreWarnings(…)`, which stops the warnings that report configuration that may be deliberate, named in
  `TenantryWarnings`: 2007 and 2008. It throws for any other id. See
  [Turning off a warning](docs/diagnostics.md#turning-off-a-warning).
- `ITenantContext<TKey>.RequiredTenant` (Tenantry.Core), the current tenant, which throws
  `TenantNotResolvedException` when none is current, for code that must not run without one. Use it where code wrote
  `CurrentTenant!`, which throws a `NullReferenceException` without a tenant.
- `tenant.ValidateTenantActivity<TValidator>()` (Tenantry.Core) adds an `ITenantActivityValidator<TKey>` of your own
  as a singleton, for a suspension check that needs services. See
  [Suspended and inactive tenants](docs/tenant-stores.md#suspended-and-inactive-tenants).
- Warning 2007, `StringTenantIdCollation`, in `Tenantry.EfCore`: with `string` tenant ids on SQL Server or MySQL, a
  model with tenant-owned tables where neither the `TenantId` column, the table nor the model sets a collation is
  logged once as it is built, naming those tables. The database's default collation there ignores case, so tenants
  `acme` and `ACME` would read and change each other's rows. Any collation the provider applies (with Oracle's MySQL
  provider, only `ForMySQLHasCollation`, as it does not apply `UseCollation`), or
  `IgnoreWarnings(TenantryWarnings.StringTenantIdCollation)`, turns it off. See
  [String tenant ids](docs/efcore-integration.md#string-tenant-ids-and-the-databases-collation).
- Docs: a second per-tenant database beside the one `AddDbContextPerTenantDatabase` connects, through `AddDbContext`
  ([A second database per tenant](docs/efcore-integration.md#a-second-database-per-tenant)); and notes that
  `AddDbContextFactory` by default, and `AddDbContext` with singleton options, keep the first tenant's connection
  string, that `IMemoryCache` and third-party bulk libraries are not isolated, that with `app.UseTenantResolution()` a
  custom resolver that reads the user finds nothing before authentication, and how to call or serve a service that
  names the tenant in a header other than `tenantry-tenant-id`. The tenant stores guide and `ITenantInvalidator<TKey>`
  now say to invalidate both tenants when an identifier moves from one to the other.

### Changed

- The `SecureApi` sample reads the tenants a caller may use from its token's `tenant_id` claims, the name
  `ResolveFromClaim` and the docs use. It read `tenant` claims.
- `UseTenantry()` on an HTTP or gRPC client now refuses a request to the client's service that already carries the
  `tenantry-tenant-id` header while no tenant is current (`InvalidOperationException`), as it did for a header naming
  another tenant while one is current. Before, such a header was sent unchanged. An untrusted client could set it on
  a request to an endpoint that allows a missing tenant, and header propagation would forward it to a service that
  trusts this one, which then acted for the tenant it named. To call as a tenant, make it current with `MakeCurrent`
  or `RunInScopeAsync` rather than setting the header.
  See [Which requests carry it](docs/http-propagation.md#which-requests-carry-it).
- Two registration errors now stop the host as it starts, rather than failing the first request or job that meets
  them: an HTTP or gRPC client with `UseTenantry()` in an application without `AddHttpPropagation()`, or with no
  address to send the tenant to; and `AddDbContextPerTenantDatabase` with an `ITenantConnectionStringProvider<TKey>`
  registered as scoped or transient. A service provider built without a host still reports them on first use.
- TNY1001 counts a context whose `DbSet<T>` or `modelBuilder.Entity<T>()` is of a type parameter constrained to a
  tenant-owned type as one with tenant-owned types, so the other types it maps with a `TenantId` are now reported. It
  also reads generated code for its contexts, types and markers, so an `IsSharedAcrossTenants()` in a generated file
  now counts; nothing in generated code is reported.
- TNY1002 also reports `IgnoreQueryFilters()` on a query of a shared entity that brings in a tenant-owned one in the
  same expression: through an `Include` or `ThenInclude` (a lambda, or a string path), a `Select`, `SelectMany`,
  `Join` or `GroupJoin`, the other query of a `Union`, `Concat`, `Intersect` or `Except`, or a navigation or query in
  one of its lambdas; and a call on a type parameter constrained to a tenant-owned type, as in a generic repository.
  `db.Categories.IgnoreQueryFilters().Include(c => c.Purchases)`, where `Category` is shared and `Purchase` is
  tenant-owned, reads every tenant's purchases, and was not reported. A query composed across statements, kept in a
  local or chosen with a conditional is not followed. Its title is now "A query that reads a tenant-owned entity
  ignores the tenant filter", and its message names the tenant-owned type the query reads. A build that treats
  warnings as errors fails on the new reports. On EF Core 10, a call that names only filters other than
  `TenantryQueryFilters.Tenant` is still not reported. See [TNY1002](docs/analyzers.md#tny1002).

### Fixed

- `app.UseTenantry()` before `app.UseRouting()` logs event 1007 when routing chooses an endpoint whose route value
  `ResolveFromRouteValue` could not read. Before, it was logged only for an endpoint with `RequireTenant()` or
  `AllowMissingTenant()`, so without `RequireTenantByDefault()` a request to `/{tenant}/orders` ran without a tenant
  and nothing was logged above Debug.
- `app.UseTenantResolution()` before `app.UseRouting()` logs event 1016 when routing chooses an endpoint whose route
  value `ResolveFromRouteValue` could not read. Before, nothing was logged, and authentication ran without the route's
  tenant: with no tenant's settings, or with the tenant a later resolver named.
- Events 1007 and 1008 are also logged when a resolver that could not read its value yet (`ResolveFromRouteValue`
  before routing, `ResolveFromClaim` before authentication) was followed by one that named a tenant, which then won.
- `TenantIsolationViolationException`'s `ExpectedTenantId`, `OffendingTenantId` and message, events 2001 and 2003,
  and the messages of `TenantNotFoundException` and `TenantInactiveException` format tenant ids with the invariant
  culture, as the `TenantId` log scope and the `tenant.id` tag do. Before, they used the current culture, so a negative
  number, or a strongly typed id that formats by culture, could read differently there.
- `RunInScopeAsync` with an empty string id said `'' is the default value of String`, which is not true of an empty
  string. It now says the id is reserved for "no tenant", as `ITenantInvalidator<TKey>` does, and so does the error for
  a tenant descriptor with such an id.
- Events 1005 and 1012 name a signed-in user without a name claim by its name identifier or `sub` claim, or as
  `(unnamed)`. Before, they logged it as `(anonymous)`, as for a request with no user.
- Behaviour the docs misstated: with `app.UseTenantResolution()`, a signed-in request whose tenant was current
  during authentication, and which the access validators refuse, is refused on every endpoint, not only on those that
  require a tenant (the ASP.NET Core and access control guides, and the comments on `ITenantAccessValidator<TKey>`,
  `ValidateTenantAccess` and `TenantResolutionOptions<TKey>`); a cancelled `ITenantInvalidator<TKey>` call stops
  before the next handler, and several handler failures are thrown as an `AggregateException`; SQL Server's default
  collation ignores case but not accents; and event 1009 is logged under a fourth category,
  `Tenantry.AspNetCore.OutputCache`.

## [0.7.0] - 2026-10-05

### Upgrading from 0.6

- `Tenantry.EfCore` and `Tenantry.AspNetCore` now carry analyzers ([Analyzers](docs/analyzers.md)). Three report
  warnings, so a project that treats warnings as errors fails to build where they find something: TNY1001 (an entity
  with a `TenantId` that is not tenant-owned), TNY1002 (`IgnoreQueryFilters()` on a tenant-owned entity) and TNY2001
  (the tenant resolved from the request with no access validator). Fix the code, or set the rule's severity in
  `.editorconfig` where the code is meant (`dotnet_diagnostic.TNY1002.severity = none`). TNY1003, TNY3001 and TNY3002
  are info.
- With `app.UseTenantResolution()`, an application whose `app.UseAuthorization()` is between it and `app.UseTenantry()`
  now fails to start: authorization there ran on the tenant the request names, before the access validators checked
  it, so a policy that reads the tenant could let in a caller who may not use it. Move `app.UseAuthorization()` after
  `app.UseTenantry()`. The authorization middleware added there some other way refuses each request with `500` and
  event 1013 (`AuthorizationBeforeTenantry`). Both checks read keys ASP.NET Core does not document; event 1014
  (`AuthorizationMarkersMissing`) warns at startup if the running version does not set them.
- With `app.UseTenantResolution()`, a signed-in request whose tenant the access validators refuse is now refused with
  the access-denied response (`403` by default) on every endpoint, including those that do not require a tenant, where
  it used to run with no tenant. Every scheme that signs out locally (each cookie scheme) is signed out on that request,
  so a `SessionStore` loses that session, and the response sets no cookie that authentication set. A caller with no
  identity and no claims, such as an anonymous one, is treated as before. A user signed in to one tenant, with one
  cookie name shared across subdomains, is therefore refused on every page of another tenant, its sign-in page included,
  until they sign out. Name the cookie per tenant (`Configure<CookieAuthenticationOptions>` in `ConfigurePerTenant`),
  and such a user is anonymous there instead.
- `ITenantContextSetter<TKey>.Use(tenant)` is now `MakeCurrent(tenant)`, and `UseNoTenant()` is
  `MakeNoTenantCurrent()`. Replace `.Use(` with `.MakeCurrent(` where it is called on the tenant context, and
  `UseNoTenant` with `MakeNoTenantCurrent`. A class of your own that implements the interface renames both methods.
- In a transaction without savepoints, or a `TransactionScope`, where a save relied on a tenant check, a concurrency
  conflict of any save that sent statements now stops the commit, even when an interceptor of yours suppresses it
  (a "last write wins" policy, say). Tenantry hears of the conflict before your interceptor decides, so it cannot tell
  a suppressed conflict from one that left rows written. Use a transaction with savepoints for such a unit of work.
- Tenantry now throws a failed tenant check that other statements rely on before every `ThrowingConcurrencyException`
  hook of yours, wherever it is registered, and refuses an unsafe commit before every `TransactionCommitting` hook of
  yours. A hook that expected to see either first no longer does.
- A many-to-many join row saved without the tenant-owned rows it joins tracked throws
  `TenantIsolationViolationException`: one added through the join entity's set with key values only, or a join entity
  of your own attached on its own. Load or attach the ends and change the relationship through their navigations.
- A save that writes many-to-many join rows with a tenant-owned end and no current tenant throws
  `TenantNotResolvedException` under the default `OnMissingTenant = Reject`, as for tenant-owned entities. 0.6 wrote
  such rows.
- A many-to-many join entity of your own that is not tenant-owned, joins a tenant-owned type and has a key of its own
  that leaves out its foreign key to that type is refused, as is one that names a tenant-owned type through an
  alternate key without its `TenantId`. Key the join entity by its two foreign keys, EF Core's default, or implement
  `ITenantEntity<TKey>` on it. A save that changes join rows also writes back the `TenantId` of each tenant-owned end
  it does not otherwise write, and runs in a transaction.
- An `ITenantConnectionStringProvider<TKey>` of your own registered as scoped or transient makes the first context
  from `AddDbContextPerTenantDatabase` throw. Register it as a singleton, and have it create a scope for any scoped
  service it needs.
- `IsolateCaches()` throws for a `HybridCache` that is not a singleton. Register it with `AddHybridCache()`, which
  makes it one.
- `UseInMemoryStore` and `InMemoryTenantStore<string>` throw `ArgumentException` for two tenant ids that differ only
  in case, such as `acme` and `ACME`. Give each tenant an id that differs from every other in more than case.
- Give each HTTP client with `UseTenantry()` an absolute `BaseAddress` in its registration, or pass its service's
  address to `UseTenantry(address)`, as a gRPC client must. `UseTenantry()` on an HTTP client gained an optional
  parameter, so a library compiled against 0.6 that calls it must be compiled again.
- A resolver from `UseResolver(sp => …)` is created per request. For one resolver for the application's lifetime,
  pass an instance to `UseResolver(resolver)`. The request's scope owns what the factory returns and disposes it, so
  a factory must not return a shared resolver, such as a singleton from the container.
- Keep one of `UseConnectionStrings(options => …)` and `UseConnectionStrings(sp => …)`, or an
  `ITenantConnectionStringProvider<TKey>` registered before `AddTenantry`. Code that resolved
  `TenantConnectionStringProvider<TKey>` resolves `ITenantConnectionStringProvider<TKey>`.
- An `OnRejected` handler or log alert that looked for a suspended tenant under `TenantRejectionReason.AccessDenied`
  or event 1005 looks for `TenantRejectionReason.Inactive` or event 1012. Dashboards that count suspended tenants'
  requests by the `tenantry.resolution.result` trace tag or metric value look for `inactive`, which was
  `access_denied`.
- A class of your own that implements `ITenantInvalidator<TKey>` adds `InvalidateLocallyAsync` and
  `InvalidateAllLocallyAsync`.
- Code that calls `TenantModel.FindUnisolatedEntityTypes` gets the roots of hierarchies only, and no owned types or
  many-to-many join entity types that hold only their two foreign keys, which follow the types they belong to.
  `TenantModel.IsSharedAcrossTenants` is true for a type whose base type is marked.
- In a transaction without savepoints (SQL Server with multiple active result sets, or a `TransactionScope` on any
  provider) where a save writes owned rows in their own table, an entity mapped to more than one table or many-to-many
  join rows, any save that failed after sending a statement now stops the commit with
  `TenantIsolationViolationException` of kind `TransactionRolledBack`, or aborts the scope, whichever save it was.
  Code that catches a failed save (a unique key, a foreign key, a concurrency conflict it retries) and goes on in the
  same transaction must run the unit of work again in a new transaction, or use a transaction with savepoints: turn
  multiple active result sets off, or begin the transaction with `Database.BeginTransaction` rather than a
  `TransactionScope`.

### Added

- `tenant.BroadcastInvalidations(sp => …)`, `ITenantInvalidator<TKey>.InvalidateLocallyAsync` and
  `InvalidateAllLocallyAsync` (Tenantry.Core), to clear a tenant on every instance of the application. The handler
  `BroadcastInvalidations` registers publishes each invalidation to the other instances, after this instance is
  cleared; each instance applies what it receives with the local methods, which do not run it, so nothing is published
  twice. See [Several instances](docs/tenant-stores.md#several-instances).
- `EfCoreIsolationOptions.OnUnmarkedEntityType` (Tenantry.EfCore), for an application that wants every entity type
  that is not tenant-owned marked `[SharedAcrossTenants]` or `IsSharedAcrossTenants()`. An entity type that is not
  tenant-owned is shared by every tenant, as before, and the default, `Allow`, checks nothing. With `Warn`, a model
  that has tenant-owned types and unmarked ones logs event 2006; with `Reject`, its first query or save throws
  `TenantIsolationViolationException` of kind `ModelConfiguration`, naming them. See
  [Entity types that are not tenant-owned](docs/efcore-integration.md#entity-types-that-are-not-tenant-owned).
- `TenantRejectionReason.Inactive` and `TenantResolutionOptions<TKey>.InactiveTenantStatusCode` (Tenantry.AspNetCore).
  A request for a tenant `ValidateTenantActivity` refuses is rejected with the reason `Inactive`, event 1012
  (`TenantInactive`) and the resolution result `inactive`, where it was `AccessDenied`. The status stays `403
  Forbidden` with the access-denied response unless you set `InactiveTenantStatusCode`, for example to `402 Payment
  Required` for a lapsed subscription. The access validators run before the activity check, so a caller they refuse
  is denied access whether or not the tenant is suspended. `TenantRejectedContext<TKey>.Tenant` is the suspended
  tenant.
- `tenant.TagRequestMetrics()` (Tenantry.AspNetCore) tags ASP.NET Core's request metric, `http.server.request.duration`,
  with the request's tenant as `tenant.id`, or with a value of your own per tenant to keep the series few. It replaces
  Tenantry.Pro's `AddTenantMetrics()`, whose package, Tenantry.Pro.AspNetCore, existed only for it. See
  [Diagnostics](docs/diagnostics.md#request-metrics-per-tenant).
- Analyzers in `Tenantry.EfCore` and `Tenantry.AspNetCore`, which an application gets with the packages: TNY1001, an
  entity with a `TenantId` that is not tenant-owned, in a context with tenant-owned types; TNY1002,
  `IgnoreQueryFilters()` on a tenant-owned entity; TNY1003, raw SQL on `Database`; TNY2001, the tenant resolved from
  the request with no access validator; TNY3001, `MakeCurrent` or `CreateScope` given a descriptor built in the call;
  TNY3002, blocking on `RunInScopeAsync`. A build that treats warnings as errors fails on those that are warnings. See
  [Analyzers](docs/analyzers.md) for each rule and how to configure it.
- Docs: [For AI coding agents](docs/ai-agents.md), the steps a coding agent follows to add Tenantry to an
  application, a test that shows one tenant cannot read or write another's rows, the mistakes agents make and the
  correct form of each, and rules to copy into an application's AGENTS.md or CLAUDE.md.

### Changed

- `ITenantContextSetter<TKey>.Use` and `UseNoTenant` are renamed `MakeCurrent` and `MakeNoTenantCurrent`, which say
  what they do. Elsewhere in .NET, `Use…` names configuration, as in `app.UseTenantry()`.
- `UseResolver(sp => …)` creates the resolver in each request's scope, as `UseResolver<TResolver>()` does. It was a
  singleton, so a factory that passed it a scoped service such as a `DbContext` shared one instance across requests.
- `UseConnectionStrings(options => …)` throws when another provider is already set, by `UseConnectionStrings(sp => …)`
  or by the application before `AddTenantry`, and `UseConnectionStrings(sp => …)` throws after
  `UseConnectionStrings(options => …)`. The delegates were ignored in the first case, and replaced in the second.
- `TenantConnectionStringProvider<TKey>` is internal. Resolving it directly bypassed any decorator.

### Fixed

- Security: with `app.UseTenantResolution()`, a request that named a tenant the caller may not use went on, without the
  tenant, with a user authenticated while that tenant was current. An authentication event or claims transformation that
  added claims from the current tenant (its plan, say) gave them to the user, and authorization granted on them on any
  endpoint that does not require a tenant. This shipped in 0.6.0. Such a request is now refused, through the usual
  rejection (`OnRejected`, problem details, metrics, event 1005), and no further middleware runs. Tenantry signs out
  every scheme that signs out locally, including a cookie under a remote default scheme such as OpenID Connect, so no
  cookie handler renews the user, in the cookie or in a `SessionStore`, and the refused response carries none of the
  cookies set after `app.UseTenantResolution()`. A sign-out that fails is logged as event 1015 (`SignOutFailed`), and
  the request is still refused. A renewed cookie (as Identity's security stamp check renews it) therefore cannot carry
  that user to a tenant they may use. Applications without `app.UseTenantResolution()` were not affected: their
  authentication runs with no tenant current. See
  [Authentication per tenant](docs/authentication-per-tenant.md#how-the-two-steps-work).
- Tenantry's own reads no longer return to the caller's synchronization context, so a desktop app that waits on a
  store read, an activity check or a connection string on its UI thread no longer deadlocks. The work passed to
  `RunInScopeAsync` still starts on the caller's context, and the scope's services are still disposed there, so await
  `RunInScopeAsync` rather than block on it. See [Desktop apps](docs/non-http-hosts.md#desktop-apps).
- Per-tenant options are built from the store's copy of the tenant whenever the store holds its id. Before, until the
  tenant was first invalidated, the first copy current when a value was built decided it for everyone: a copy made
  current with `MakeCurrent` or `CreateScope` whose fields differed from the store's (an older one, or one built by
  hand) set the tenant's options for every later request. A value for an id the store does not hold is now built on
  each read and not kept, where before each such id added an entry kept until an invalidation; so is a value for an id
  the store answers with another tenant's (matching without regard to case), which invalidating the store's id would
  not clear. The store read that builds a value runs on the thread pool when a synchronization context or a task
  scheduler other than the default is current, so a store that awaits without `ConfigureAwait(false)` no longer waits
  for the thread the read blocks.
- A save that adds, changes or deletes owned rows in their own table through an owner it does not otherwise write
  refuses the owner when the `TenantId` it holds in memory is not the current tenant's. 0.6.0 confirmed such an owner by
  writing its `TenantId` back, and wrote the value the owner held: when change detection had not seen that value
  change (it was set after the last `DetectChanges`, or with `AutoDetectChangesEnabled` off), a tenant could move its
  own row, with its owned rows, to another tenant. It could not reach another tenant's existing rows, whose stored
  `TenantId` the write-back's `WHERE` still required. The join rows of a many-to-many relationship, confirmed the same
  way, are refused the same way.
- A save that failed after sending statements in a transaction EF Core cannot undo it in is counted as failed even when
  every notice of its failure is stopped by an interceptor registered before `UseTenantry()` that throws from it (as
  EntityFramework.Exceptions does from `SaveChangesFailed`), or by a `SaveChangesFailed` handler subscribed before
  Tenantry's that throws. Tenantry's save notices, command and transaction hooks are now interceptors of EF Core's
  internal service provider, which EF Core runs before every interceptor added with `AddInterceptors`. Before, a
  failed save nested in another, or run from the `SavedChanges` of a save that sent nothing (`SuppressWithResult`),
  could be confirmed and its rows committed. See
  [Saves that succeed or fail as a whole](docs/efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole).
- With `CacheTenants`, a store read that began before a tenant was invalidated and ended after it is no longer cached,
  even for an instant. Before, its answer was written to the cache and then removed, and a lookup in between, such as
  one that builds the tenant's options, could read the copy the invalidation had replaced and keep what it built from
  it.
- `UseTenantry()` on an HTTP or gRPC client with no base address in its registration fails when the client is created.
  It sent the tenant's id to any host the client called. `UseTenantry(address)` names the service for a client that
  sets its address elsewhere.
- A transaction that EF Core cannot undo a failed save in (an ambient `TransactionScope`, or SQL Server with multiple
  active result sets) is rolled back if any save in it failed after sending statements, once a save in it has written
  owned rows in their own table or an entity mapped to more than one table. Tenantry counts the saves of each such
  transaction and how many reported success, rather than matching each of EF Core's notices to a save. Before, a later
  save, or a save run inside another that EF Core reported as saved and then as failed, could mark a failed save as
  succeeded, and the commit kept the rows the failed save had written. A failed command and a failed tenant check are
  noted as they happen, so an interceptor that translates the failure, as EntityFramework.Exceptions does, no longer
  keeps it from Tenantry. A failed save that wrote no such rows itself now also stops the commit when another save in
  the transaction did, and so does a save EF Core reported as saved and then as failed. A failure notice counts every
  save still under way that sent a statement as failed, so a failed save is not missed when the notice is taken for
  another, such as an audit save a validation interceptor stopped.
- A transaction handed from one context to another after a failed save in it is still refused at the commit. Before,
  the context that let it go was taken to have ended it.
- A save that an interceptor runs from another save's `SavingChanges` no longer sets back the transaction (with
  `AutoTransactionBehavior.Never`) or the savepoints (with `AutoSavepointsEnabled = false`) that Tenantry turned on for
  the save around it, which could then leave rows written after their tenant check failed.
- A save that adds, changes or deletes a join row of a many-to-many relationship confirms that each tenant-owned row it
  joins is the current tenant's, as stored. A join row carries no tenant, and EF Core writes join rows for ends that
  are themselves unchanged, so through stubs that carried another tenant's keys with the current tenant's `TenantId`,
  a save could add, change or delete that tenant's join rows. An end the save does not otherwise write has its
  `TenantId` written back with its concurrency token, once per save, and a join row saved without its tenant-owned
  ends tracked is refused. A join entity with a key of its own that leaves out a foreign key to a tenant-owned end, or
  one that names a tenant-owned end through an alternate key without its `TenantId`, is refused. See
  [Many-to-many relationships](docs/efcore-advanced.md#many-to-many-relationships).
- The first context from `AddDbContextPerTenantDatabase` throws for an `ITenantConnectionStringProvider<TKey>`
  registered as scoped or transient, before or after `AddTenantry`. It was accepted before, and the singleton context
  factory then kept one instance of it, with its scoped dependencies, for the application's lifetime, or failed scope
  validation on the first context. A scoped provider registered before `AddTenantry` and replaced by
  `UseConnectionStrings(sp => …)` no longer leaves the replacement scoped.
- `IsolateCaches()` throws for a `HybridCache` registered as scoped or transient. It accepted one before, and
  invalidating a tenant then failed, because invalidation clears the cache outside any scope.
- After a save that relied on a tenant check, any failed transaction operation stops the commit unless EF Core reports
  it as a commit, a rollback, or the creation or release of a savepoint, none of which leaves the failed save's rows in
  the transaction. Before, only a failed rollback to the save's savepoint did, matched by EF Core's name for it, which
  EF Core does not document: under another name, the commit would have kept the rows.
- `UseInMemoryStore` refuses two `string` tenant ids that differ only in case. A database whose collation ignores case
  (the default on SQL Server and MySQL) takes them for one id, so each tenant's query filter matched the other's rows
  and its updates and deletes could change them. See
  [String tenant ids and the database's collation](docs/efcore-integration.md#string-tenant-ids-and-the-databases-collation)
  for what a store of your own must guarantee.

## [0.6.0] - 2026-10-03

### Upgrading from 0.5

0.6 adds to the API more than it changes it. Most applications build and run as before; check these:

- Code that resolved `EfCoreIsolationOptions` from DI reads `IOptions<EfCoreIsolationOptions>`. To let maintenance code
  write without a tenant, give it a context of its own, registered with `options.UseTenantry(o => …)`, rather than
  relaxing the application's options.
- A class of your own that implements `ITenantContextSetter<TKey>` adds `UseNoTenant()`.
- A tenant header or query parameter sent more than once names no tenant. A proxy that sets the tenant header must
  replace the client's, not add another.
- `UseInMemoryStore` reads its tenants when it is called, and refuses duplicate ids and the ids Tenantry reserves for
  "no tenant" there.
- With trimming or Native AOT, `UseTenantry()`, `AddDbContextPerTenantDatabase` and `IsSharedAcrossTenants()` warn
  where you call them, as EF Core's own methods do.
- A post-configuration of an options type you configure per tenant sees the tenant's values.
- If an access validator refuses suspended tenants, `ValidateTenantActivity` also stops their background work.
- `ITenantStoreCache<TKey>` is gone. Inject `ITenantInvalidator<TKey>` instead and await `InvalidateAsync(tenantId)` or
  `InvalidateAllAsync()`, which also clear everything else Tenantry keeps for the tenant.

### Added

- `TenantIds`: `Format` writes a tenant id with the invariant culture, `TryParse` reads one back and refuses the ids
  Tenantry reserves for "no tenant", and `IsReserved` tells those ids apart. They are what Tenantry itself uses, for code
  of your own that carries tenant ids as text.
- `TenantTelemetry`: the `tenant.id` tag and `TenantId` log-scope names Tenantry records a tenant under, and
  `CreateLogScope`, the scope's state, to record it the same way in your own code.
- `TenantPropagation.HeaderName` (`tenantry-tenant-id`), the header that carries a tenant between processes. It was
  Tenantry.Pro's; Tenantry.Http and Tenantry.Pro's integrations now read it from Tenantry.Core.
- `Tenantry.Http`, a new package: `UseTenantry()` on an `HttpClient` or gRPC client sends the current tenant's id
  with its requests, in the `tenantry-tenant-id` header, after `tenant.AddHttpPropagation()` in `AddTenantry`. Only
  requests to the client's base address carry it. While a tenant is current, a request that already carries the header
  with another tenant's id throws `InvalidOperationException`. `ConfigureHttpClientDefaults` is refused. See
  [Calling other services](docs/http-propagation.md).
- `ResolveFromPropagationHeader(isTrustedCaller)` (Tenantry.AspNetCore) resolves the tenant another service sent, by
  id with the store's `GetTenantAsync`, so a store whose identifiers are slugs still finds it. It reads the header only
  when `isTrustedCaller` accepts the request, after authentication, and ignores it from any other caller.
- `Tenantry.Caching`, a new package: `IsolateCaches()` keeps the application's `HybridCache` entries per tenant (keys
  and tags under the tenant's prefix, a factory run as the tenant, and no call without a tenant), with
  `SharedHybridCache` for entries every tenant shares and `ITenantDistributedCache` for code that uses
  `IDistributedCache` directly. Keyed `HybridCache` registrations are kept per tenant the same way. Invalidating a
  tenant removes its entries. The host does not start when a `HybridCache` registered after `AddTenantry` would share
  entries across tenants. See [Caching per tenant](docs/caching.md).
- `IsolateOutputCache()` (Tenantry.AspNetCore) makes cached responses vary by tenant, and invalidating a tenant
  evicts its responses. A response for a request `UseTenantry()` did not handle first is not cached (log event 1009,
  once).
- `Tenantry.Options`, a new package: `tenant.ConfigurePerTenant(perTenant => perTenant.Configure<TOptions>(…))` makes
  `IOptionsSnapshot<T>` and `IOptionsMonitor<T>` give the current tenant's value, built from the ordinary
  configuration and the tenant, cached per tenant and cleared when the tenant is invalidated. `IOptions<T>` keeps the ordinary value, so a singleton that
  reads it once never keeps one tenant's settings; reading it while a tenant is current logs a warning (event 3001),
  once per options type. See [Options per tenant](docs/per-tenant-options.md).
- `ITenantInvalidator<TKey>`: `InvalidateAsync(tenantId, ct)` and `InvalidateAllAsync(ct)` remove the cached tenant
  and run every registered `ITenantInvalidationHandler<TKey>`, with or without `CacheTenants`, so one call clears
  everything kept for a tenant. Handlers are asynchronous and take a `CancellationToken`, so removing a tenant's
  entries from a remote cache does not block a thread. Tenantry.Caching, `IsolateOutputCache()` and Tenantry.Options
  register one. It replaces `ITenantStoreCache<TKey>`, whose synchronous methods blocked on those handlers, and
  refuses an id Tenantry reserves for "no tenant" (`Guid.Empty`, `0`, an empty string), which no tenant has.

- `options.UseTenantry(o => …)` sets a context's own isolation options, starting from the application's, so a
  context kept for maintenance code can allow writes without a tenant while every other context keeps `Reject`.

- `ValidateTenantActivity(t => …)` and `ITenantActivityValidator<TKey>`: one check for whether work may run for a
  tenant, so suspending a tenant stops all its work. `app.UseTenantry()` refuses an inactive tenant as it refuses one
  an access validator refuses, `RunInScopeAsync` throws the new `TenantInactiveException`, and other code, Tenantry.Pro's
  background services, schedulers and message integrations among it, asks the new `ITenantActivity<TKey>`.
  `CreateScope` does not check, so migrations and provisioning still reach suspended tenants.

- `UseConnectionStrings(sp => provider)` registers an `ITenantConnectionStringProvider<TKey>` built from the
  application's services, for connection strings read with a client registered in DI, and
  `DecorateConnectionStrings((sp, inner) => …)` wraps whichever provider is registered, before or after it, for
  caching or logging.
- `ITenantConnectionStringProvider<TKey>.CanGetSynchronously` (default `true`). When it is `false`, as for
  `UseConnectionStrings` with only `GetConnectionStringAsync`, the scoped context of `AddDbContextPerTenantDatabase`
  reads its connection string when it first opens a connection. It can now be injected; only asynchronous EF Core
  calls work on it. Before, injecting it threw.

- Seams for packages that build on Tenantry, Tenantry.Pro among them. They and the other extension points
  (`TenantIds`, `ITenantRegistration`, the EF Core contributors, `TenantConnectionStringProvider<TKey>`) are marked
  `[EditorBrowsable(EditorBrowsableState.Advanced)]`, and the API reference lists them apart from the types an
  application uses:
  - `TenantContextGuard` (Tenantry.EfCore), an interceptor base that checks a context before it opens a connection,
    runs a command or saves, and the violation kind `TenantSchemaMismatch`, for a context on another tenant's schema.
    A guard checks a save before Tenantry stamps its new entities, wherever it is among the context's interceptors,
    so a save it refuses leaves them as they were.
  - `TenantModel` (Tenantry.EfCore): `HasTenantOwnedEntityTypes`, `IsTenantOwned`, `IsSharedAcrossTenants` and
    `FindUnisolatedEntityTypes`; and `[SharedAcrossTenants]` or `IsSharedAcrossTenants()` to mark an entity type every
    tenant shares. Marking a tenant-owned type fails the model check.
  - `ITenantKeyType`, registered by `AddTenantry`, and `services.FindTenantKeyType()`: the tenant key type, for code
    that has only a service provider or collection, with an AOT-safe visitor.
  - `TenantryAspNetCoreTelemetry`: the activity source, meter and log category names of `app.UseTenantry()`.

- Pack checks each package's API against the last release (`TenantryPackageBaseline`, 0.5.0), so a patch release
  cannot break code compiled against an earlier one in its minor, as Tenantry.Pro's version range assumes.

- `Configure<TOptions>(name, …)` and `ConfigureAll<TOptions>(…)` in `ConfigurePerTenant` (Tenantry.Options) configure
  named options per tenant, such as an authentication scheme's, which its handler reads with
  `IOptionsMonitor<T>.Get(scheme)`.

- `app.UseTenantResolution()` (Tenantry.AspNetCore) resolves the tenant before `app.UseAuthentication()`, so
  authentication handlers read the tenant's options, and `app.UseTenantry()` after it runs the access validators. Only
  the resolvers added before the first one that needs the user (a claim resolver or `ResolveFromPropagationHeader`) run
  before authentication, so the registration order still decides which resolver wins. A tenant the validators refuse is
  not current for the rest of the request, an endpoint that `app.UseTenantry()` did not run for gets `500` (event 1011),
  and a pipeline without `app.UseTenantry()` fails to start. Event 1010 warns when it runs after authentication. See
  [Authentication per tenant](docs/authentication-per-tenant.md).

- Docs: ASP.NET Core Identity in a database tenants share, with each tenant's users kept apart and user names unique
  within a tenant, now tested ([ASP.NET Core Identity](docs/aspnetcore-identity.md)); and a scheme per tenant, for
  tenants on different identity providers, through a policy scheme
  ([A scheme per tenant](docs/authentication-per-tenant.md#a-scheme-per-tenant)).

### Changed

- Tenantry.Options runs the tenant's steps after every `Configure` and before every `PostConfigure`, through an options
  factory of its own. Before, they ran as post-configurations, so one added earlier ran before them.
- `ITenantContextSetter<TKey>.UseNoTenant()` makes no tenant current until it is disposed, as `Use(tenant)` makes one
  current. A class of your own that implements `ITenantContextSetter<TKey>` must add it.
- `UseTenantry()`, `AddDbContextPerTenantDatabase` and `IsSharedAcrossTenants()` carry `[RequiresUnreferencedCode]`
  and `[RequiresDynamicCode]`, as EF Core's `DbContext` does, so the analyzers warn where an app calls them. Before,
  Tenantry.EfCore relied on EF Core's own annotations. See [AOT & trimming](docs/aot-and-trimming.md).
- `ConfigureEfCoreIsolation` configures `IOptions<EfCoreIsolationOptions>`, so `services.Configure` sets the same
  options, and the options can no longer be changed through a registered instance at run time. The
  `TenantNotResolvedException` for a write without a tenant now points at a maintenance context, not the global switch.

### Fixed

- `app.UseTenantry()` before `app.UseRouting()` no longer lets a request without a tenant reach an endpoint that
  requires one: the request is rejected as it would be with routing first. Before, it ran without a tenant and
  event 1007 was logged.
- `ResolveFromHeader`, `ResolveFromQueryString` and `ResolveFromPropagationHeader` resolve nothing from a header or
  parameter sent more than once. Before, they took the first value, so a client's header could win over one a proxy
  appended.
- `UseInMemoryStore` and `InMemoryTenantStore` refuse a tenant with an id Tenantry reserves for "no tenant", or two
  tenants with the same id, when they are created. Before, the first request failed with a 500.

## [0.5.0] - 2026-10-03

### Upgrading from 0.4

0.5 reshapes the public API once, before 1.0. Registration code needs no Tenantry `using` directive any more;
entity and handler code needs `using Tenantry;` (and `using Tenantry.EfCore;` for the EF Core types).

| 0.4 | 0.5 |
|-----|-----|
| `AddTenantryCore<TKey>(…)` (Core) and `AddTenantry<TKey>(…)` (AspNetCore) | `AddTenantry<TKey>(…)`, in Core, for every host |
| `IAspNetCoreTenantBuilder<TKey>` | `ITenantBuilder<TKey>`; the ASP.NET Core methods are extension methods on it |
| namespaces `Tenantry.Core`, `.Core.Exceptions`, `.Core.Stores` | `Tenantry` |
| `Tenantry.AspNetCore.Attributes`, `.Resolution` | `Tenantry.AspNetCore` |
| `Tenantry.Core.Extensions`, `Tenantry.AspNetCore.Extensions`, `Tenantry.EfCore.Extensions` | `Microsoft.Extensions.DependencyInjection`, `Microsoft.AspNetCore.Builder`, `Microsoft.EntityFrameworkCore` (no `using` needed) |
| `ITenantScope<TKey>.BeginScope(tenant)` | `ITenantContextSetter<TKey>.Use(tenant)` |
| `ITenantServiceScope<TKey>` (what `ITenantScopeFactory` creates) | `ITenantScope<TKey>` |
| `ITenantScoped<TKey>` / `TenantScoped<TKey>` | `ITenantEntity<TKey>` (get-only `TenantId`) / `TenantEntity<TKey>` |
| `ITenantStoreAccessor<TKey>` | `ITenantLookup<TKey>` |
| `ITenantConnectionStringResolver<TKey>.Resolve(tenant)` / `ResolveAsync(tenant)` | `ITenantConnectionStringProvider<TKey>.Get(tenant)` / `GetAsync(tenant)` |
| `ITenantConnectionStringResolver<TKey>.Resolve()` / `ResolveAsync()` (current tenant) | `CurrentTenantConnectionString<TKey>.Get()` / `GetAsync()` |
| `services.AddTenantConnectionStrings<TKey>(…)` | `tenant.UseConnectionStrings(…)` inside `AddTenantry` |
| `Tenantry.Core.MissingTenantBehavior` (`Allow`, `Warn`, `Reject`, `Skip`) | `Tenantry.EfCore.MissingTenantBehavior` (`Reject`, `Warn`, `Allow`) |
| `EfCoreIsolationOptions.DetectSpoofedWrites` | removed: a new entity that names another tenant is always rejected |
| `Tenantry.Core.Exceptions.TenantIsolationViolationException.EntityTypeName` | `Tenantry.EfCore.TenantIsolationViolationException.TypeName`, plus `Kind` |
| `MultiTenantDbContext<TKey>`, or `ITenantAwareDbContext<TKey>` with `modelBuilder.ApplyTenantFilters<TKey, TContext>(this)`, and `options.AddTenantInterceptors(sp)` | `options.UseTenantry()` on a plain `DbContext` |
| `tenant.AddEfCoreIsolation(o => …)` | optional: `tenant.ConfigureEfCoreIsolation(o => …)` |
| `services.AddTenantDbContextPool<TContext, TKey>((sp, o) => o.UseSqlServer().AddTenantInterceptors(sp))` | `tenant.AddDbContextPerTenantDatabase<TContext>((sp, o) => o.UseSqlServer(), pooled: true)` |
| `AddDbContext` reading the current tenant's connection string in its options callback | `tenant.AddDbContextPerTenantDatabase<TContext>((sp, o) => o.UseSqlServer())` |
| the EF Core 10 tenant filter named `__TenantryFilter__` | named `TenantryQueryFilters.Tenant` (`"Tenantry.Tenant"`) |

### Added

- CI builds every ```csharp block in the README and docs against the packed packages
  (`scripts/check-doc-snippets.sh`), so a guide can no longer show code that does not compile; a block that is
  not meant to compile is marked ```csharp no-compile. CI also starts every sample in Development, where the
  host validates its registrations, and sends a tenant request to the web ones (`scripts/smoke-samples.sh`).
- An `.editorconfig`, checked in CI with `dotnet format --verify-no-changes`.
- `options.UseTenantry()`, one call that isolates any `DbContext`, pooled or not, with no base class or
  interface: it adds the tenant query filters while EF Core builds the model, after `OnModelCreating` (so the
  order of your own configuration no longer matters), and the save interceptor and bulk-update guard. It reads
  the tenant through the context's application service provider, and a Tenantry error names the missing
  `AddTenantry<TKey>` on the first query or save.
- `tenant.AddDbContextPerTenantDatabase<TContext>(…, pooled)` registers a context for a database per tenant,
  pooled or not, connecting each one to the current tenant's database, and checks at registration that
  `UseConnectionStrings` was called. It replaces `AddTenantDbContextPool` and the hand-written non-pooled recipe;
  the pooled variant uses EF Core's public `PooledDbContextFactory` instead of rebuilding EF Core's registration.
  It applies `UseTenantry()` before your configuration, so your interceptors (an audit log) see new entities
  stamped, and a context that is not pooled has its scope as its application service provider, as with
  `AddDbContext`.
- On EF Core 10 the tenant filter is named `TenantryQueryFilters.Tenant`, so
  `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` removes it alone. EF Core does not allow a named filter beside
  an unnamed one, so an entity's unnamed filter of its own is named `TenantryQueryFilters.Application`: each can be
  ignored alone, and `Reload()` and `GetDatabaseValues()` ignore it, as EF Core documents.
- `EfCoreIsolationOptions.OnSaveWithoutTransaction` (`SaveWithoutTransactionBehavior.UseTransaction`, the default, or
  `Reject`, which throws `TenantIsolationViolationException` of the new kind `SaveWithoutTransaction`), for a save
  that must succeed or fail as a whole while `Database.AutoTransactionBehavior` is `Never`; the kind
  `TransactionRolledBack`, for a transaction Tenantry rolls back instead of committing; log events 2004
  (`TransactionNotCommitted`) and 2005 (`SaveInTransaction`).
- `ITenantDbContextOptionsContributor` and `ITenantModelContributor`, so packages that build on Tenantry can add
  to the options and model of every context that uses `UseTenantry()`.
- Contexts that use `UseTenantry()` check each model on their first query and first save, and throw
  `TenantIsolationViolationException` instead of running either when a tenant-scoped entity type has lost its
  tenant query filter or its `TenantId` concurrency token after Tenantry built the model (a convention, a model
  customizer in a custom internal service provider, a compiled model).
- Conformance tests for each package: a host that registers the package's features through its public
  methods, with a scoped store, validates scopes and every registration on build, resolves every Tenantry
  service and starts.
- `TenantNotFoundException`, a `TenantNotResolvedException` that carries the `TenantId` that was looked up.
  `RunInScopeAsync` throws it for an id the store does not have, so a queue consumer can tell a message for a
  deleted tenant from code that runs without a tenant.
- `TenantResolutionOptions<TKey>` is public, configured with `tenant.ConfigureResolution(o => …)`: the status codes
  of each rejection (`MissingTenantStatusCode`, `TenantNotFoundStatusCode`, `AccessDeniedStatusCode`),
  `RequireTenantByDefault`, and two events: `OnResolved`, when a request's tenant is made current, and
  `OnRejected`, when a request is rejected, which is told the reason (`Missing`, `NotFound`, `AccessDenied`), the
  identifier and the refused tenant, and can change the status code or write its own response (a redirect) with
  `HandleResponse()`.
- Rejections are written as problem details (`application/problem+json`) when an `IProblemDetailsService` is
  registered (`builder.Services.AddProblemDetails()`).
- `ResolveFromSubdomain(o => …)` takes `BaseDomains` (only `<tenant>.<base domain>` resolves, and
  `acme.localhost` works in development with `localhost`) and `IgnoredSubdomains` (`www` by default); a host that
  is an IP address resolves nothing, and the subdomain is returned in lower case.
- Identifiers: a resolver returns an identifier, and `ITenantStore<TKey>.FindByIdentifierAsync` finds the tenant it
  names. By default it parses the identifier as the key type, with the invariant culture, and calls
  `GetTenantAsync`, so existing stores need no change; a store implements it to map slugs or custom domains to
  `Guid` or `int` tenants. `ITenantLookup<TKey>.FindByIdentifierAsync` calls it from a scope of its own,
  and the middleware finds request tenants through it. `ResolveFromHost(o => o.ExcludedDomains.Add(…))` resolves the
  request's host name, for tenants with domains of their own (`localhost` is excluded by default). Both host
  resolvers compare and return international domain names in their ASCII form.
- `tenant.CacheTenants(o => o.Duration = …)` caches the tenants Tenantry reads (the middleware's and
  `ITenantLookup`'s lookups, by id and by identifier) in memory, 5 minutes by default, with no new
  dependency; `ITenantStoreCache<TKey>.Invalidate(id)` and `InvalidateAll()` remove them when a tenant changes
  (`AddTenantry` always registers it, as Tenantry.Pro does `IConnectionStringCache`). A lookup that finds no tenant is
  not cached. It reads the time from a registered `TimeProvider`.
- `ITenantAccessValidator<TKey>` and `tenant.ValidateTenantAccess<TValidator>()`: an access validator created in
  each request's scope, so it can use a `DbContext`. The delegate overloads remain; all run in the order they
  were added.
- Your own tenant type: `tenant.As<AppTenant>()` reads a tenant as the type your store returns, in any delegate
  that receives one (connection strings, access validators, Tenantry.Pro's selectors), and
  `ITenantContext<TKey>.GetCurrentTenant<AppTenant>()` reads the current one. Both throw an error naming both
  types when the store returns another type.
- Diagnostics (`docs/diagnostics.md`): the middleware's and the EF Core isolation's log messages have stable
  event ids (1001–1008 under `Tenantry.AspNetCore`, 2001–2005 under `Tenantry.EfCore`; 2001 is an isolation
  violation), written with `[LoggerMessage]`. While a request's tenant is current, its trace span is tagged
  `tenant.id` and a log scope with `TenantId` is open (the names Tenantry.Pro's jobs and messages use). The
  `Tenantry.AspNetCore` activity source has a `Tenantry.ResolveTenant` span, and its meter counts requests by
  result in `tenantry.resolutions`.
- When routing chooses an endpoint with Tenantry's metadata after the middleware ran, or the authentication
  middleware runs after it and signs in a user whose claim `ResolveFromClaim` reads, the middleware logs a warning
  once.
- `CurrentTenantConnectionString<TKey>`, the current tenant's connection string.
- `TenantIsolationViolationException.Kind` (`EntityWrite`, `BulkUpdate`, `TenantDatabaseMismatch`,
  `ModelConfiguration`), and the tenant ids on a database-per-tenant mismatch, which were empty.
- `ITenantBuilder`, the builder without its key type, and `ITenantRegistration`, for packages whose builder
  methods take a type parameter of their own. Both are trimming- and Native AOT-safe (no `MakeGenericType`).
- The `Aot` sample uses every ASP.NET Core builder method, problem details and connection strings, so CI
  publishes all of them with Native AOT.

### Changed

- **Breaking:** the API reshape in [Upgrading from 0.4](#upgrading-from-04). There is one entry point and one
  builder, so every builder method chains (`tenant => tenant.ResolveFromHeader(…).UseStore<T>().UseConnectionStrings(…)`),
  and `UseResolver<TResolver>()` returns the builder without its key type (call it last). It creates the resolver
  in each request's scope, so it can depend on scoped services.
- **Breaking:** an application registers one store and one tenant key type. A second `UseStore`/`UseInMemoryStore`
  (or an `ITenantStore<TKey>` registered directly), and `AddTenantry` with another key type, throw.
- **Breaking:** `app.UseTenantry()` checks the registration when the pipeline is built (a resolver and a store,
  and a Tenantry-worded error when `AddTenantry` registered no request resolution), in place of the hosted service
  that checked at startup. Creating `ITenantLookup` without a store throws, so a worker's hosted service that
  depends on it fails as the host starts.
- **Breaking:** the resolution middleware no longer echoes the request's identifier, and a rejection's body is
  empty (or problem details, above) instead of plain text. An endpoint that does not require a tenant is never
  rejected: a request whose identifier names no tenant, or names one an access validator refuses, continues
  without a tenant, so `www.` hosts and health probes no longer get `404`. With access validators, a tenant that
  does not exist gets the access-denied response, so a caller cannot tell which tenants exist. An identifier that
  does not parse as the key type names no tenant (`404`, not `400`), and a resolver that returns an empty string
  has no identifier, so the next resolver is tried. The store is read through `ITenantLookup`, in a scope
  of its own, not the request's.
- **Breaking:** a web application that registers request resolution but does not call `app.UseTenantry()` fails to
  start, instead of running every endpoint, those that require a tenant included, without one.
- **Breaking:** `ITenantDescriptor<TKey>` derives from a new non-generic `ITenantDescriptor`, which holds `Name`
  (`As<TTenant>()` extends it), so an explicit implementation is written `string ITenantDescriptor.Name`.
  `ITenantLookup<TKey>` has a new member, `FindByIdentifierAsync`, which a hand-written implementation or fake
  must add. `ITenantContext<TKey>` is no longer covariant in `TKey` (variance never applied: the constraints rule out
  every conversion), so it can declare `GetCurrentTenant<TTenant>()`.
- **Breaking:** the request's log scope holds only `TenantId` (formatted with the invariant culture), not
  `TenantName`, and the middleware and the EF Core isolation log under the categories `Tenantry.AspNetCore` and
  `Tenantry.EfCore` instead of their internal type names.
- **Breaking:** a new entity that names another tenant is always rejected with
  `TenantIsolationViolationException` (it was silently moved to the current tenant unless `DetectSpoofedWrites`
  was on). The stamp goes through EF Core, so `ITenantEntity.TenantId` needs only a getter.
- **Breaking:** the key type's default (`Guid.Empty`, `0`, an empty string) is reserved for "no tenant":
  `ITenantContextSetter.Use`, `ITenantScopeFactory.CreateScope` and `RunInScopeAsync` throw `ArgumentException` for
  it, and an identifier that parses to it names no tenant. It could write but never read its own rows.
- `TenantNotResolvedException`'s default message names every way to make a tenant current, not only
  `app.UseTenantry()`, and the EF Core error for a write without a tenant names `RunInScopeAsync`.
- The packages depend on each other from this release up to the next minor (`[0.5.0, 0.6.0)`) instead of exactly:
  they no longer share internals, so one can be updated within the minor without the others.

- **Breaking:** EF Core isolation is `options.UseTenantry()` (see Upgrading), which always applies the query
  filters and the interceptors together. A context that attached the interceptors but left the filters out, for
  example to read across tenants, used to save with a warning and read every tenant's rows; use
  `IgnoreQueryFilters()` for deliberate cross-tenant reads.
- **Breaking:** a model that Tenantry cannot isolate fails to build, instead of being left unisolated: entities
  that implement `ITenantEntity` with a key type other than the registered one (0.4 skipped them silently), with
  more than one key type, a tenant-scoped type whose base type or owner is not tenant-scoped, a `TenantId` that is
  not a mapped public property (an explicit interface implementation failed with an `ArgumentException`), and, on
  EF Core 10, a filter of your own named `TenantryQueryFilters.Tenant`. Creating a context that also replaces
  `IModelCustomizer`, which Tenantry's own customizer would otherwise override, or uses `UseInternalServiceProvider`,
  which never gets Tenantry's customizer, throws.
- `Tenantry.EfCore` depends on `Microsoft.EntityFrameworkCore.Relational` only (it brings
  `Microsoft.EntityFrameworkCore`), and has no API annotated for dynamic code of its own.
- `ExecuteUpdate` fails closed on setters the guard cannot read. Their expression shape is undocumented and
  changes between EF Core versions, so a version whose shape Tenantry does not know is now rejected instead of
  its setters going unchecked. Tests pin the shape of each supported version (8, 9, 10, and the 11 release
  candidate).
- The interceptor no longer logs a warning when `TenantId` is not a concurrency token: the model check throws
  instead.
- A tenant store returns every tenant that exists, suspended ones included; whether a tenant may be served
  is decided by an access validator for HTTP requests and by your own code for background work. The docs
  and the `EfCoreWeb` sample hid inactive tenants from the store (a `404`), which made tools that maintain
  every tenant's database, such as Tenantry.Pro's migrations, skip them. The sample now keeps them in the
  store and refuses them with a validator (`403`); see "Suspended and inactive tenants" in
  `docs/tenant-stores.md`. Nothing checks a tenant's status for you in background work.
- `Tenantry.EfCore` no longer depends on `Microsoft.Extensions.DependencyInjection.Abstractions` itself: EF Core
  and `Tenantry.Core` bring it, EF Core at the same minimum as before.
- The packages carry a README of their own, with links that work on NuGet.org, an icon, the project URL
  (tenantry.dev) and a copyright notice, and their descriptions match what each package holds. Packing checks
  that each target framework's assembly keeps the API of the lower ones (package validation).
- A GitHub release's notes are its section of this changelog.

### Fixed

- A tenant-scoped entity mapped to more than one table (table-per-type inheritance, entity splitting) could be
  changed in another tenant's row through a stub with a forged `TenantId`: EF Core updates only the tables whose
  columns changed, and `TenantId`'s concurrency token is checked only in its own table, so a change to another
  table's columns matched the row by its key alone. Its `TenantId` is now written back to its table too, in EF
  Core's transaction, so the database checks it (and, when EF Core does not save `TenantId` after an insert, its
  stored row is read before the save); one keyed by its `TenantId` needs neither. The same
  went for a stub deleted and added again under the same key, which EF Core saves as an `UPDATE` of what differs,
  table by table: the deleted one's stored row is now read. Owners and these pairs were also not found for a byte
  array key, which Tenantry compared by reference: keys are now compared as EF Core compares them.
- Owned rows in a table of their own, and the rows of an entity mapped to more than one table outside the table with
  `TenantId`, rely on another statement of the save for their tenant check: the owner's, or the one on `TenantId`'s
  table. A forged write of them stayed written wherever a failed save was not undone as a whole: with
  `Database.AutoTransactionBehavior` set to `Never` (on a provider that batches statements, whatever their order), in a
  transaction without savepoints (SQL Server with MARS, or `AutoSavepointsEnabled` off) or a `TransactionScope` that
  the application committed after catching the exception, and, in any setup, when an interceptor suppressed the
  concurrency failure. A deleted entity over more than one table relied on this alone. Tenantry now makes such a save
  succeed or fail as a whole: the check's failure cannot be suppressed; without a transaction, EF Core runs the save
  in one (or, with `OnSaveWithoutTransaction = Reject`, it throws before anything is sent); savepoints are turned on
  for it; and a transaction without them, or a `TransactionScope`, in which such a save failed after sending some of
  its statements is rolled back instead of committed, as the save may have failed before its check was read.
- The stored row Tenantry reads before some saves (an owner, or an entity over more than one table, whose `TenantId`
  is not written back, and a deleted and added pair over more than one table) was read through the tenant filter, and
  on EF Core 8 and 9, or for an unnamed filter of the application's on EF Core 10, through that filter too, so the
  current tenant's row that it hid (a soft-deleted one) could not be changed. The read now names the tenant itself and
  ignores every query filter.
- A tenant-scoped owned type mapped to JSON crashed a save that read its stored row (EF Core 8 and 9), or failed to
  build the model with EF Core's own error (EF Core 10). It now fails to build the model on every version, saying
  why: its owner's row holds it, under the owner's `TenantId`.
- An entity type that is not tenant-scoped could share a tenant-scoped entity's table (table splitting), with no tenant
  filter or `TenantId`, and read or change every tenant's rows of it. Such a model now fails on its first query or
  save.
- `Entry(…).Reload()` and `GetDatabaseValues()` read a row by its key without query filters (EF Core's behaviour),
  so an entity attached with another tenant's key, as in a forged write that fails with
  `DbUpdateConcurrencyException`, got that tenant's values. Tenantry now keeps the tenant filter on that query:
  another tenant's row reads as deleted (`GetDatabaseValues()` returns `null`, `Reload()` detaches the entity). On
  EF Core 8 and 9 the entity's own filter, merged with the tenant filter, applies to these reads as well.
- An owned type that does not implement `ITenantEntity<TKey>` (an owned value object in its own table, or in its
  owner's row) was not checked through its owner: through an attached stub of another tenant's owner, a tenant
  could add, change or delete that tenant's owned rows, and an owned entity attached without its owner, whatever
  its type, was saved unchecked; without a tenant, `Reject` let such writes through. Every owned entity is now
  checked through its owner (its owner's `TenantId` is written back with its concurrency token, so an audit log sees
  an update of the owner), an owned entity saved without its owner is rejected, and `OnMissingTenant` treats owned
  entities of a tenant-scoped owner as tenant-scoped. Such a type keyed without its owner
  (`OwnsMany(…, b => b.HasKey(x => x.Id))`) fails to build the model: its rows carry no tenant, so an update or
  delete by its key would reach another tenant's row whatever owner it was attached under.
- The middleware and `ValidateTenantAccessByClaim` parsed identifiers and claim values with the current culture, so
  a numeric id could parse differently, or not at all, on a server with another culture. They use the invariant
  culture, as Tenantry.Pro's jobs and messages do.
- The API reference repeated a type parameter's variance in its constraints (`where TKey : IEquatable<out TKey>`),
  which is not C#.
- The documented asynchronous access validator used a field (`_entitlements`) that a top-level `Program.cs` cannot
  have; the docs now show a validator class with its own dependencies, and a delegate that resolves its service
  from the request.
- A tenant could add rows to another tenant's owned collection (`OwnsMany`): attaching a stub of the other tenant's
  owner, with its own or no `TenantId`, and adding an owned entity saved it, and the owner's tenant then read the
  row, because EF Core reads owned rows through their owner without a tenant filter and does not write an owner
  that is only attached. When a save adds an owned entity to such an owner, the owner is now rejected if it was
  attached as another tenant, and otherwise its `TenantId` is written back with its concurrency token in the same
  transaction, so a forged one matches no row and nothing is saved. An audit log sees that as an update of the
  owner. The same check covers an owned entity with a key of its own moved to another owner by changing its foreign
  key, the nearest tenant-scoped owner of a nested owned entity, an owner marked modified with nothing EF Core writes
  (no UPDATE carries its token), and an owner deleted and added again under the same key in one save (EF Core sends
  one UPDATE of what differs, which can be nothing; its stored row is read). An owner whose `TenantId` is part of the key its
  owned types are owned through needs no write, as their foreign key names the tenant; one whose `TenantId` EF Core
  does not write after an insert (in another key, such as an alternate key, or configured so) is read before the
  save instead. Owning a type through a key of a tenant-scoped owner that neither includes
  nor is part of its primary key, nor includes `TenantId`, fails to build the model.
- The documented order, `base.OnModelCreating` (and so `ApplyTenantFilters`) first, lost the tenant filter: on
  EF Core 8 and 9 a later `HasQueryFilter` replaced it, so the tenant's queries returned every tenant's rows,
  and an entity type configured later got no filter; on EF Core 10 the model failed to build. `UseTenantry()`
  adds the tenant filter after `OnModelCreating`.
- A tenant-scoped inheritance hierarchy failed to build its model, because Tenantry gave the derived types a
  filter of their own, which EF Core allows only on the root. The root's filter now covers the
  hierarchy, and a tenant-scoped type whose base type is not tenant-scoped throws a Tenantry error. A
  tenant-scoped owned type, which also failed to build, gets its `TenantId` concurrency token and is filtered
  through its owner, which must be tenant-scoped too (otherwise it throws).
- Calling `AddTenantry` twice registered a second set of resolution options, so the first call's access
  validators and `RequireTenantByDefault` were silently dropped. Calling `AddEfCoreIsolation` (now
  `ConfigureEfCoreIsolation`) twice ignored the second call's options. Both now configure the options already
  registered.
- `MissingTenantBehavior.Allow` said EF Core saves writes without a stamped `TenantId`, and troubleshooting
  said the same of `Warn`; both let updates and deletes through, but a new entity without a `TenantId` still
  throws.
- `docs/access-control.md` documented `ValidateTenantAccessAny`, which was removed before 0.4.0. It now
  shows OR logic written in one validator.
- The getting-started `DbContext` did not compile with `Guid` keys (`Guid? CurrentTenantId`), and several
  guides left out the using directives their code needs.
- The README suggested the interceptor alone isolates a plain `DbContext`; it stamps and checks writes but
  does not filter reads, which also needs the query filters.
- Behaviour the docs misstated: without a tenant, `SaveChanges` throws by default (`Reject`) rather than
  logging a warning; `MissingTenantBehavior`'s documentation named the wrong default and said EF Core accepts
  `Skip`.
- The `EfCoreWeb` sample did not start: its migration has `TenantId` indexes that its model never declared
  (the model now declares them, as the EF Core guide recommends), and its seed data referenced categories by
  an id they did not have yet. The sample `.http` files carried the old product name.

## [0.4.0] - 2026-09-29

The first beta release. Tenantry stays on 0.x releases until 1.0; a minor release can change the API, and
this changelog says how to update. This release includes everything in the 0.3.0-alpha.1 development build.

### Added

- An API reference, `docs/api`: a page for every public type and its members, generated from the XML
  documentation comments by `scripts/generate-api-docs.sh` (docfx metadata, then `scripts/api-docs.cs`), and
  published with the docs. CI fails when the pages do not match the source, or when a public parameter or
  type parameter has no description; every one now has one.
- DbContext pooling with a database per tenant. `AddTenantDbContextPool<TContext, TKey>` registers a
  scoped context and `IDbContextFactory<TContext>` over one pool and connects every lease to the current
  tenant's database. EF Core keeps a pooled context's connection string between leases, so a guard also
  rejects a pooled context whose connection was not set for its current lease or belongs to another tenant.
  `Tenantry.EfCore` now depends on `Microsoft.EntityFrameworkCore.Relational`.
- Per-tenant connection strings for a database per tenant. `UseConnectionStrings` (or
  `AddTenantConnectionStrings`) takes a synchronous and/or asynchronous delegate, and the singleton
  `ITenantConnectionStringResolver<TKey>` returns the current tenant's connection string or a given
  tenant's. It does not cache, and rejects an empty value. `TenantConnectionStringResolver<TKey>` is public
  so other resolvers can wrap it. New `DatabasePerTenant` sample.
- Worker scopes for background work. `ITenantScopeFactory<TKey>.CreateScope(tenant)` opens a fresh DI
  scope with the tenant active; disposing it, with `using` or `await using`, disposes its services while
  the tenant is still active and then restores the previous tenant in the disposing code. For work that
  starts from a tenant id, `RunInScopeAsync(tenantId, work, ct)` looks the tenant up and runs the work
  inside its scope. There is deliberately no `CreateScopeAsync`: an `async` method cannot change its
  caller's ambient tenant. `ITenantStoreAccessor<TKey>` lets singletons read a scoped store without
  capturing it. `AddTenantryCore` and `AddTenantry` register both as singletons.
- `Tenantry.Samples.SecureApi`, a production-shaped API: JWT authentication, tenant selection validated
  against the caller's `tenant` claims (403 otherwise), required tenants (400 without one) and EF Core
  isolation, with integration tests. The header-only quick starts are labelled as introductory.
- DbContext pooling (`AddDbContextPool`, `AddPooledDbContextFactory`). `MultiTenantDbContext` has an
  options-only constructor that reads Tenantry's ambient tenant context, so a pooled instance isolates
  whichever tenant is active each time it is used. Call `AddTenantInterceptors(sp)` in the registration
  callback of a pooled context.

### Security

- `ExecuteUpdate` can no longer set `TenantId` on a tenant-scoped entity. The query filter limited which
  rows a bulk update touched but not the values it wrote, so `SetProperty(o => o.TenantId, other)` moved
  every row the current tenant could see into another tenant. A guard now resolves each setter the way
  EF Core does, by member access or `EF.Property`, through casts and through `Select`, `Join` and
  `SelectMany` projections (EF Core maps `Select(o => new { T = o.TenantId })` then
  `SetProperty(x => x.T, other)` to `TenantId`), and throws `TenantIsolationViolationException` before
  running. It fails closed on setters it cannot resolve. `AddTenantInterceptors` and
  `MultiTenantDbContext` register the guard automatically.
- `UPDATE` and `DELETE` now only affect rows stored under the tenant an entity was loaded or attached
  with. Previously a caller could overwrite, reassign or delete another tenant's row by attaching an
  entity with that row's primary key and the current tenant's `TenantId`; `DetectSpoofedWrites` did not
  prevent it. `ApplyTenantFilters` now marks `TenantId` as a concurrency token, so such a write matches no
  row and EF Core throws `DbUpdateConcurrencyException`.
- `Modified` and `Deleted` entities must have been loaded or attached as the current tenant, not only carry
  its `TenantId`. An entity loaded under one tenant and saved after switching scope now throws
  `TenantIsolationViolationException`.

### Fixed

- Disposing a tenant scope twice, or out of order, no longer restores a tenant whose scope has already
  closed. Disposing a scope that is not the innermost one in the current flow (out of order, or from
  another async flow) closes it without changing the active tenant; the nearest scope that is still open
  is restored when the innermost one closes. If a child task disposes a handle it inherited, the caller's
  own later disposal still restores the caller's previous tenant.

### Changed

- `Tenantry.EfCore` needs EF Core 8.0.31, 9.0.20 or 10.0.12 or later within that major (previously 8.0.10,
  9.0.0 and 10.0.0), the oldest versions the tests run against. Microsoft.Extensions dependencies take a
  minimum only, from the target framework's own major, instead of stopping before the next major: a .NET 8
  app can use current Azure SDKs, which need Microsoft.Extensions 10.x.
- `Tenantry.EfCore` and `Tenantry.AspNetCore` depend on exactly the same version of `Tenantry.Core`
  (`[x.y.z]`) instead of a minimum, because `Tenantry.AspNetCore` uses Core internals. Update the
  Tenantry packages together.
- The integration suite runs the write-isolation checks against SQL Server 2022, PostgreSQL 16 and MySQL
  8.4 (Oracle's EF Core provider) on .NET 10; the EF Core guide lists the tested combinations.
- README, the EF Core guide, troubleshooting and XML docs describe isolation as it is enforced: in EF Core
  rather than by the database, provider-agnostic but tested on SQLite and SQL Server, with raw SQL and
  `IgnoreQueryFilters()` bulk writes outside it.
- `ApplyTenantFilters` no longer claims to configure a `TenantId` index; it never did. Index `TenantId`
  yourself, usually as the leading column of composite indexes (see the EF Core guide).
- `TenantIsolationViolationException` has a constructor for violations detected before any tenant value
  is known; `OffendingTenantId` and `ExpectedTenantId` are empty in that case.
- The EF Core guide documents what is and isn't isolated: raw SQL and `IgnoreQueryFilters()` are
  unisolated by design.
- **Breaking:** `EfCoreIsolationOptions.OnMissingTenant` defaults to `Reject`. Saving tenant-scoped entities
  without a resolved tenant throws `TenantNotResolvedException`; previously the default `Warn` let such a
  save update or delete any tenant's rows by key and insert rows with no tenant.
- `OnMissingTenant` only applies when a save writes tenant-scoped entities. Saves of host-level data (the
  tenant registry, global reference data) no longer need a tenant under any policy.
- Under `Warn` and `Allow`, a new tenant-scoped entity must set `TenantId` explicitly; an unowned row is
  rejected.
- Setting `OnMissingTenant` to `Skip` or an undefined value throws `ArgumentOutOfRangeException`. `Skip`
  previously behaved like `Allow` for writes.
- A tenant-scoped `Modified` or `Deleted` entity with a `null` `TenantId` throws
  `TenantIsolationViolationException` instead of `NullReferenceException`.
- The interceptor logs a warning when a tenant-scoped write matches no row, and once per entity type when
  `TenantId` is not a concurrency token (the context did not call `ApplyTenantFilters`).
- The next EF Core migration you add records the `TenantId` concurrency token in the model snapshot. No
  columns change.

## [0.2.3-alpha] - 2026-06-27

### Changed

- Internal refactoring, code-quality fixes, documentation and dependency updates.
- Tags `v0.2.0-alpha` to `v0.2.2-alpha` point at the same commit as each other and were not published to
  NuGet; 0.2.3-alpha is the first 0.2 release on NuGet.

## [0.1.0-alpha] - 2026-06-21

### Added

- First preview of `Tenantry.Core`, `Tenantry.AspNetCore` and `Tenantry.EfCore`: generic tenant keys,
  tenant resolution and stores, ASP.NET Core middleware and access validation, non-HTTP tenant scopes,
  and EF Core query filters with a `SaveChanges` isolation interceptor.
