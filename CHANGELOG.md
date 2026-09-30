# Changelog

All notable changes to Tenantry will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- CI builds every ```csharp block in the README and docs against the packed packages
  (`scripts/check-doc-snippets.sh`), so a guide can no longer show code that does not compile; a block that is
  not meant to compile is marked ```csharp no-compile. CI also starts every sample in Development, where the
  host validates its registrations, and sends a tenant request to the web ones (`scripts/smoke-samples.sh`).
- An `.editorconfig`, checked in CI with `dotnet format --verify-no-changes`.
- The tenant interceptors check each model on its first query and first save, and throw
  `TenantIsolationViolationException` instead of running either when a tenant-scoped entity type has lost its
  tenant query filter or its `TenantId` concurrency token. This catches configuration that ran after
  `ApplyTenantFilters`: on EF Core 8 and 9, a `HasQueryFilter` call after it replaced the tenant filter, so the
  tenant's queries returned every tenant's rows, and an entity type added after it got no filter at all.
- Conformance tests for each package: a host that registers the package's features through its public
  methods, with a scoped store, validates scopes and every registration on build, resolves every Tenantry
  service and starts.

### Changed

- **Breaking:** a context with the tenant interceptors (`AddTenantInterceptors`, or a non-pooled
  `MultiTenantDbContext`) must apply the tenant filters to every tenant-scoped entity type. A context that
  attached the interceptors but left the filters out, for example to read across tenants, used to save with a
  warning and read every tenant's rows; it now throws on its first query or save. Apply the filters and use
  `IgnoreQueryFilters()` for deliberate cross-tenant reads. Only the filter `ApplyTenantFilters` adds is
  recognised, not a tenant filter written by hand.
- Call `ApplyTenantFilters`, or `base.OnModelCreating` in a `MultiTenantDbContext`, at the **end** of
  `OnModelCreating`, after your own configuration. The docs said to call the base first, which is the order
  that lost the tenant filter (see Added); the guides, samples and XML documentation now show it last.
- `ApplyTenantFilters` throws `TenantIsolationViolationException` for an entity that implements
  `ITenantScoped` with a key type other than its own `TKey`, which it used to skip silently, leaving the
  entity with no isolation at all. The model check rejects such an entity too.
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

### Fixed

- A tenant-scoped inheritance hierarchy failed to build its model, because `ApplyTenantFilters` gave the
  derived types a filter of their own, which EF Core allows only on the root. The root's filter now covers the
  hierarchy, and a tenant-scoped type whose base type is not tenant-scoped throws a Tenantry error. A
  tenant-scoped owned type, which also failed to build, gets its `TenantId` concurrency token and is filtered
  through its owner, which must be tenant-scoped too (otherwise it throws).
- Calling `AddTenantry` twice registered a second set of resolution options, so the first call's access
  validators and `RequireTenantByDefault` were silently dropped. Calling `AddEfCoreIsolation` twice ignored the
  second call's options. Both now configure the options already registered.
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
  logging a warning; the subdomain resolver takes the first label of any host with three or more (so
  `www.example.com` resolves to `www`); `MissingTenantBehavior`'s documentation named the wrong default and
  said EF Core accepts `Skip`.
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
