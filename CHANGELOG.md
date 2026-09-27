# Changelog

All notable changes to Tenantry will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- DbContext pooling (`AddDbContextPool`, `AddPooledDbContextFactory`). `MultiTenantDbContext` has an
  options-only constructor that reads Tenantry's ambient tenant context, so a pooled instance isolates
  whichever tenant is active each time it is used. Call `AddTenantInterceptors(sp)` in the registration
  callback of a pooled context.

### Security

- `ExecuteUpdate` can no longer set `TenantId` on a tenant-scoped entity. The query filter limited which
  rows a bulk update touched but not the values it wrote, so `SetProperty(o => o.TenantId, other)` moved
  every row the current tenant could see into another tenant. It now throws
  `TenantIsolationViolationException` before running. `AddTenantInterceptors` and `MultiTenantDbContext`
  register the guard automatically.
- `UPDATE` and `DELETE` now only affect rows stored under the tenant an entity was loaded or attached
  with. Previously a caller could overwrite, reassign or delete another tenant's row by attaching an
  entity with that row's primary key and the current tenant's `TenantId`; `DetectSpoofedWrites` did not
  prevent it. `ApplyTenantFilters` now marks `TenantId` as a concurrency token, so such a write matches no
  row and EF Core throws `DbUpdateConcurrencyException`.
- `Modified` and `Deleted` entities must have been loaded or attached as the current tenant, not only carry
  its `TenantId`. An entity loaded under one tenant and saved after switching scope now throws
  `TenantIsolationViolationException`.

### Changed

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
