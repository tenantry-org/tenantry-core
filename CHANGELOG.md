# Changelog

All notable changes to Tenantry will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` removes it alone. An entity with an unnamed filter of its
  own gets the tenant filter merged into it instead, and Tenantry logs this once per model.
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
  event ids (1001–1008 under `Tenantry.AspNetCore`, 2001–2004 under `Tenantry.EfCore`; 2001 is an isolation
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

### Fixed

- `Entry(…).Reload()` and `GetDatabaseValues()` read a row by its key without query filters (EF Core's behaviour),
  so an entity attached with another tenant's key, as in a forged write that fails with
  `DbUpdateConcurrencyException`, got that tenant's values. Tenantry now keeps the tenant filter on that query:
  another tenant's row reads as deleted (`GetDatabaseValues()` returns `null`, `Reload()` detaches the entity). On
  EF Core 8 and 9, and for an entity whose own filter is unnamed, the entity's own filter applies to these reads as
  well.
- An owned type that does not implement `ITenantEntity<TKey>` (an owned value object in its own table, or in its
  owner's row) was not checked through its owner: through an attached stub of another tenant's owner, a tenant
  could add, change or delete that tenant's owned rows, and an owned entity attached without its owner, whatever
  its type, was saved unchecked; without a tenant, `Reject` let such writes through. Every owned entity is now
  checked through its owner (its owner's `TenantId` is written back with its concurrency token, so an audit log sees
  an update of the owner), an owned entity saved without its owner is rejected, and `OnMissingTenant` treats owned
  entities of a tenant-scoped owner as tenant-scoped.
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
  owner.
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
