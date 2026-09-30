# Troubleshooting

Common symptoms and what causes them. Most isolation surprises come down to one of: no tenant in scope,
the wrong `TKey`, middleware ordering, or the interceptor not attached.

## Startup fails with "no tenant resolvers were registered" / "no tenant store was registered"

`AddTenantry` validates configuration at startup. You called `AddTenantry` but forgot a resolver or a
store.

- Add at least one resolver: `tenant.ResolveFromHeader(...)`, `ResolveFromClaim(...)`, etc., or
  `UseResolver(...)`.
- Add exactly one store: `tenant.UseInMemoryStore(...)` or `tenant.UseStore<T>()`.

(`AddTenantryCore` does not perform this validation — see [Non-HTTP hosts](non-http-hosts.md).)

## Queries return **no** rows for a valid tenant

The query filter is fail-closed: when `CurrentTenantId` is the default value it matches nothing. Check,
in order:

1. **Is a tenant actually in scope?** Inject `ITenantContext<TKey>` and confirm `HasTenant` is true at
   the point of the query. On the web, `UseTenantry()` must have run and resolved a tenant. In a
   worker, you must be inside a scope from `ITenantScopeFactory` (or `BeginScope`).
2. **Did the request resolve a tenant?** A missing/blank header (or other source) means no tenant. If
   the endpoint should require one, add `.RequireTenant()` so you get a clear `400` instead of silent
   empties.
3. **Is the entity actually tenant-scoped?** It must implement `ITenantScoped<TKey>` with the **same**
   `TKey` you registered. A `Guid` registration plus an `ITenantScoped<string>` entity never lines up.
4. **Is the tenant id the default value?** Avoid `Guid.Empty`/`0` as a real tenant id; the fail-closed
   guard treats the default as "no tenant".

## Queries return **all** tenants' rows

- You probably called `IgnoreQueryFilters()` somewhere (directly, or via a shared queryable helper).
- The entity does not implement `ITenantScoped<TKey>`, so it is treated as global. If it should be
  isolated, implement the interface (or derive from `TenantScoped<TKey>`).
- The context has no tenant interceptors (`.AddTenantInterceptors(sp)`, or a non-pooled
  `MultiTenantDbContext`), and `ApplyTenantFilters` was not called or ran before configuration that
  replaced its filter. With the interceptors attached, such a model throws on its first query instead (see
  below).

## `TenantId` is not being stamped on insert

- The interceptor is not attached: ensure `.AddTenantInterceptors(sp)` is in the `AddDbContext`
  callback for that context, and that `AddEfCoreIsolation()` was called in the registration lambda.
- No tenant is in scope at `SaveChanges`. By default (`MissingTenantBehavior.Reject`) a save that writes
  tenant-scoped entities throws `TenantNotResolvedException` and nothing is written. `Warn` (which logs) and
  `Allow` let updates and deletes through unchecked, but a new entity must still carry its `TenantId`: without
  one the save throws. Save inside a tenant scope, or set `TenantId` yourself in deliberate cross-tenant code.

## `TenantIsolationViolationException` on save

This is the system working: a `Modified`/`Deleted` entity (or, with `DetectSpoofedWrites`, an `Added`
one with an explicit wrong id) belongs to a different tenant than the current scope. The exception's
`OffendingTenantId` and `ExpectedTenantId` tell you which.

- You loaded an entity in one tenant's scope and modified it in another's. Do tenant work inside the
  owning tenant's scope.
- You set `TenantId` manually to another tenant. Don't set it — let the interceptor stamp it.
- A legitimate cross-tenant admin operation: use a fresh `DbContext` inside the correct tenant's scope
  per tenant, or `IgnoreQueryFilters()` for reads. `SaveChanges` still validates every write against the
  current scope, but `IgnoreQueryFilters()` combined with `ExecuteUpdate`/`ExecuteDelete` affects every
  tenant, so treat it as privileged.

## `TenantIsolationViolationException`: "has no tenant query filter" or "is not a concurrency token"

The model check found a tenant-scoped entity type that would not be isolated, and stopped the query or save
before it ran.

- **No tenant query filter:** call `ApplyTenantFilters` (or `base.OnModelCreating` in a
  `MultiTenantDbContext`) at the **end** of `OnModelCreating`. An entity type configured after it gets no
  filter, and a `HasQueryFilter` after it can replace the tenant filter. Only the filter `ApplyTenantFilters`
  adds counts, not one written by hand or set by a convention or model customizer.
- **Not a concurrency token:** something after `ApplyTenantFilters` configured `TenantId` with
  `IsConcurrencyToken(false)`. Remove it: updates and deletes rely on it to match the stored tenant.
- **"implements ITenantScoped&lt;X&gt;, but the tenant key type here is Y":** the entity uses a different key
  type from `AddTenantry<Y>` or `ApplyTenantFilters<Y, …>`. Use the same key type everywhere.
- **"Owned entity … is tenant-scoped but its owner … is not":** EF Core filters owned rows only through their
  owner, so make the owner tenant-scoped too.

## Filter uses a stale tenant / leaks across requests

Almost always a `DbContext` wiring mistake. The filter must read the tenant from the **`DbContext`**,
not a captured service:

- Implement `ITenantAwareDbContext<TKey>` and expose `CurrentTenantId => _tenantContext.CurrentTenantId`,
  then `ApplyTenantFilters<TKey, TContext>(this)`. Passing `this` is what lets EF Core re-evaluate the
  tenant per query. See [EF Core integration](efcore-integration.md#how-the-query-filter-stays-correct).
- Inject `ITenantContext<TKey>` (read-only) into the context, not `ITenantScope<TKey>`.

## Claim-based resolution or validation never matches

- `UseTenantry()` runs **before** `UseAuthentication()`, so `HttpContext.User` is empty when the
  resolver/validator runs. Move `UseTenantry()` after `UseAuthentication()`.
- The claim type does not match (`ResolveFromClaim("tenant_id")` vs. the actual claim name).
- The claim value does not parse to your `TKey` (e.g. a non-GUID string for a `Guid` key).

## Route-value resolution returns null

Routing must run before the middleware so the route value exists. With `WebApplication` this is
automatic; in a custom pipeline, ensure `UseRouting()` precedes `UseTenantry()`.

## Subdomain resolution returns null on localhost

`ResolveFromSubdomain` requires at least three dot-separated host segments, so `localhost` and
`acme.localhost` resolve to `null`. Use header resolution for local development.

## Background/queued work loses the tenant

The `AsyncLocal` tenant flows down into awaited work but does not survive past the scope's disposal.
Capture the tenant **id** when enqueuing and run the deferred work with
`ITenantScopeFactory.RunInScopeAsync(id, …)`. See [Non-HTTP hosts](non-http-hosts.md#scopes-async-and-threads).

## The tenant is missing after a helper opened a scope

A scope opened inside an `async` method is not active for that method's caller: an `async` method's
changes to an `AsyncLocal` are undone when it returns. Open the scope in the method that does the work
(`await using var scope = scopes.CreateScope(tenant);`), or pass the work in with
`ITenantScopeFactory.RunInScopeAsync`.

## AOT/trim warnings from the EF Core integration

Expected. `ApplyTenantFilters` and `MultiTenantDbContext<TKey>` are annotated `[RequiresDynamicCode]`
and `[RequiresUnreferencedCode]` because query filters build expression trees. EF Core is not
AOT-compatible; do not publish an EF-Core-backed app with Native AOT. See
[AOT & trimming](aot-and-trimming.md).
