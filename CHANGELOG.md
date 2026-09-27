# Changelog

All notable changes to Tenantry will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
